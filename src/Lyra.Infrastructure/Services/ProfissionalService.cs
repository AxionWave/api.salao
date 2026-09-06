using Lyra.Application.Abstractions;
using Lyra.Application.Profissionais;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lyra.Infrastructure.Services;

public sealed class ProfissionalService(LyraDbContext db) : IProfissionalService
{
    private static readonly HashSet<string> Cores = ["copper", "ember", "ok", "warn", "destructive"];
    private static readonly TimeOnly PadraoInicio = new(9, 0);
    private static readonly TimeOnly PadraoFim = new(19, 0);

    public async Task<ListaProfissionaisResponse> ListarAsync(
        int empresaId,
        string? busca,
        bool? ativo,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;

        var q = db.Profissionais.AsNoTracking()
            .Include(p => p.Disponibilidades)
            .Where(p => p.EmpresaId == empresaId);
        if (ativo is bool flag)
        {
            q = q.Where(p => p.Ativo == flag);
        }
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            q = q.Where(p => p.Nome.ToLower().Contains(termo));
        }

        var total = await q.CountAsync(ct);
        var itens = await q
            .OrderByDescending(p => p.Ativo)
            .ThenBy(p => p.Nome)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new ListaProfissionaisResponse(itens.Select(Map).ToList(), total, page, pageSize);
    }

    public async Task<ProfissionalDto?> ObterAsync(int empresaId, int id, CancellationToken ct)
    {
        var p = await db.Profissionais.AsNoTracking()
            .Include(x => x.Disponibilidades)
            .FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct);
        return p is null ? null : Map(p);
    }

    public async Task<ProfissionalDto> CriarAsync(int empresaId, SalvarProfissionalRequest request, CancellationToken ct)
    {
        if (request.PessoaId is not > 0)
        {
            throw new ArgumentException("Cadastre a pessoa no Core antes de criar o profissional.");
        }

        var entidade = new Profissional { EmpresaId = empresaId, PessoaId = request.PessoaId };
        Aplicar(entidade, request, criar: true);
        entidade.DataCriacao = DateTime.UtcNow;
        db.Profissionais.Add(entidade);
        await SalvarAsync(ct);
        return Map(entidade);
    }

    public async Task<ProfissionalDto> AtualizarAsync(int empresaId, int id, SalvarProfissionalRequest request, CancellationToken ct)
    {
        var entidade = await db.Profissionais
            .Include(x => x.Disponibilidades)
            .FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Profissional não encontrado.");
        Aplicar(entidade, request, criar: false);
        entidade.DataAtualizacao = DateTime.UtcNow;
        await SalvarAsync(ct);
        return Map(entidade);
    }

    public async Task<ProfissionalDto> AlterarAtivoAsync(int empresaId, int id, bool ativo, CancellationToken ct)
    {
        var entidade = await db.Profissionais
            .Include(x => x.Disponibilidades)
            .FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Profissional não encontrado.");
        entidade.Ativo = ativo;
        entidade.DataAtualizacao = DateTime.UtcNow;
        await SalvarAsync(ct);
        return Map(entidade);
    }

    private async Task SalvarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out var constraint))
        {
            if (constraint.Contains("pessoa", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Esta pessoa já está vinculada como profissional nesta empresa.");
            }
            throw new InvalidOperationException("Já existe um profissional com este nome nesta empresa.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex, out string constraint)
    {
        constraint = string.Empty;
        if (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            constraint = pg.ConstraintName ?? string.Empty;
            return true;
        }
        return ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private static void Aplicar(Profissional entidade, SalvarProfissionalRequest request, bool criar)
    {
        var nome = (request.Nome ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nome))
        {
            throw new ArgumentException("Nome é obrigatório.");
        }
        if (nome.Length > 120)
        {
            throw new ArgumentException("Nome deve ter no máximo 120 caracteres.");
        }

        var cor = string.IsNullOrWhiteSpace(request.Cor) ? "copper" : request.Cor.Trim().ToLowerInvariant();
        if (!Cores.Contains(cor))
        {
            throw new ArgumentException("Cor da agenda inválida.");
        }

        entidade.Nome = nome;
        if (request.PessoaId is > 0)
        {
            entidade.PessoaId = request.PessoaId;
        }
        entidade.Cor = cor;
        entidade.Ativo = request.Ativo;

        var intervalos = NormalizarIntervalos(request.Disponibilidades);
        if (intervalos.Count == 0)
        {
            if (!criar && entidade.Disponibilidades.Count > 0)
            {
                return;
            }
            intervalos = PadraoSemanal();
        }

        entidade.Disponibilidades.Clear();
        foreach (var (dia, inicio, fim) in intervalos)
        {
            entidade.Disponibilidades.Add(new Disponibilidade
            {
                DiaSemana = dia,
                Inicio = inicio,
                Fim = fim
            });
        }
    }

    private static List<(int Dia, TimeOnly Inicio, TimeOnly Fim)> NormalizarIntervalos(
        IReadOnlyList<IntervaloDto>? lista)
    {
        if (lista is null || lista.Count == 0)
        {
            return [];
        }

        var vistos = new HashSet<int>();
        var result = new List<(int, TimeOnly, TimeOnly)>();
        foreach (var item in lista)
        {
            if (item.DiaSemana is < 0 or > 6)
            {
                throw new ArgumentException("Dia da semana deve ser 0 (domingo) a 6 (sábado).");
            }
            if (!vistos.Add(item.DiaSemana))
            {
                throw new ArgumentException("Há dois intervalos no mesmo dia.");
            }
            if (!TimeOnly.TryParse(item.Inicio, out var inicio) || !TimeOnly.TryParse(item.Fim, out var fim))
            {
                throw new ArgumentException("Horário inválido. Use HH:mm.");
            }
            if (fim <= inicio)
            {
                throw new ArgumentException("O fim do expediente deve ser depois do início.");
            }
            result.Add((item.DiaSemana, inicio, fim));
        }
        return result;
    }

    private static List<(int Dia, TimeOnly Inicio, TimeOnly Fim)> PadraoSemanal()
    {
        var lista = new List<(int, TimeOnly, TimeOnly)>();
        for (var d = 1; d <= 6; d++)
        {
            lista.Add((d, PadraoInicio, PadraoFim));
        }
        return lista;
    }

    private static ProfissionalDto Map(Profissional p) => new(
        p.Id,
        p.EmpresaId,
        p.PessoaId,
        p.Nome,
        p.Cor,
        p.Ativo,
        p.Disponibilidades
            .OrderBy(d => d.DiaSemana)
            .Select(d => new IntervaloDto(d.DiaSemana, d.Inicio.ToString("HH:mm"), d.Fim.ToString("HH:mm")))
            .ToList(),
        p.DataCriacao,
        p.DataAtualizacao);
}
