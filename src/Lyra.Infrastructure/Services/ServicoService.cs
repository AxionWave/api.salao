using Lyra.Application.Abstractions;
using Lyra.Application.Servicos;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lyra.Infrastructure.Services;

public sealed class ServicoService(LyraDbContext db) : IServicoService
{
    public async Task<ListaServicosResponse> ListarAsync(
        int empresaId,
        string? busca,
        bool? ativo,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;

        var q = db.Servicos.AsNoTracking().Where(s => s.EmpresaId == empresaId);
        if (ativo is bool flag)
        {
            q = q.Where(s => s.Ativo == flag);
        }
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            q = q.Where(s =>
                s.Nome.ToLower().Contains(termo)
                || (s.Categoria != null && s.Categoria.ToLower().Contains(termo)));
        }

        var total = await q.CountAsync(ct);
        var itens = await q
            .OrderByDescending(s => s.Ativo)
            .ThenBy(s => s.Nome)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var categorias = await db.Servicos.AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Categoria != null && s.Categoria != "")
            .Select(s => s.Categoria!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

        return new ListaServicosResponse(itens.Select(Map).ToList(), total, page, pageSize, categorias);
    }

    public async Task<ServicoDto?> ObterAsync(int empresaId, int id, CancellationToken ct)
    {
        var s = await db.Servicos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct);
        return s is null ? null : Map(s);
    }

    public async Task<ServicoDto> CriarAsync(int empresaId, SalvarServicoRequest request, CancellationToken ct)
    {
        var entidade = new Servico { EmpresaId = empresaId };
        Aplicar(entidade, request);
        entidade.DataCriacao = DateTime.UtcNow;
        db.Servicos.Add(entidade);
        await SalvarAsync(ct);
        return Map(entidade);
    }

    public async Task<ServicoDto> AtualizarAsync(int empresaId, int id, SalvarServicoRequest request, CancellationToken ct)
    {
        var entidade = await db.Servicos.FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Serviço não encontrado.");
        Aplicar(entidade, request);
        entidade.DataAtualizacao = DateTime.UtcNow;
        await SalvarAsync(ct);
        return Map(entidade);
    }

    public async Task<ServicoDto> AlterarAtivoAsync(int empresaId, int id, bool ativo, CancellationToken ct)
    {
        var entidade = await db.Servicos.FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Serviço não encontrado.");
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
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Já existe um serviço com este nome nesta empresa.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation
        || (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ?? false);

    private static void Aplicar(Servico entidade, SalvarServicoRequest request)
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
        if (request.DuracaoMinutos is < 1 or > 24 * 60)
        {
            throw new ArgumentException("Duração deve ser entre 1 e 1440 minutos.");
        }
        if (request.Preco < 0)
        {
            throw new ArgumentException("Preço não pode ser negativo.");
        }

        var categoria = string.IsNullOrWhiteSpace(request.Categoria) ? null : request.Categoria.Trim();
        if (categoria is { Length: > 80 })
        {
            throw new ArgumentException("Categoria deve ter no máximo 80 caracteres.");
        }

        entidade.Nome = nome;
        entidade.DuracaoMinutos = request.DuracaoMinutos;
        entidade.Preco = decimal.Round(request.Preco, 2, MidpointRounding.AwayFromZero);
        entidade.Categoria = categoria;
        entidade.Ativo = request.Ativo;
    }

    private static ServicoDto Map(Servico s) => new(
        s.Id,
        s.EmpresaId,
        s.Nome,
        s.DuracaoMinutos,
        s.Preco,
        s.Categoria,
        s.Ativo,
        s.DataCriacao,
        s.DataAtualizacao);
}
