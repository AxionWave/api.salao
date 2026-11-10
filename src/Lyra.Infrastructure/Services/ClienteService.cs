using System.Text.RegularExpressions;
using Lyra.Application.Abstractions;
using Lyra.Application.Clientes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Services;

public sealed class ClienteService(LyraDbContext db) : IClienteService
{
    public async Task<ListaClientesResponse> ListarAsync(
        int empresaId,
        string? busca,
        bool? ativo,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 10 : Math.Min(pageSize, 100);

        var q = db.Clientes.AsNoTracking().Include(c => c.Familia).Where(c => c.EmpresaId == empresaId);
        if (ativo is bool flag)
        {
            q = q.Where(c => c.Ativo == flag);
        }
        if (!string.IsNullOrWhiteSpace(busca))
        {
            // Cada palavra precisa aparecer em algum campo: "maria 9988" acha Maria com telefone ...9988.
            foreach (var palavra in busca.Trim().ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
            {
                var termo = palavra;
                var digitos = SomenteDigitos(termo);
                q = q.Where(c =>
                    c.Nome.ToLower().Contains(termo)
                    || (c.Email != null && c.Email.ToLower().Contains(termo))
                    || (digitos.Length > 0 && c.Telefone.Contains(digitos)));
            }
        }

        var total = await q.CountAsync(ct);
        var itens = await q
            .OrderByDescending(c => c.Ativo)
            .ThenBy(c => c.Nome)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new ListaClientesResponse(itens.Select(Map).ToList(), total, page, pageSize);
    }

    public async Task<ClienteDto?> ObterAsync(int empresaId, int id, CancellationToken ct)
    {
        var c = await db.Clientes.AsNoTracking()
            .Include(x => x.Familia)
            .FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct);
        return c is null ? null : Map(c);
    }

    public async Task<ClienteDto> CriarAsync(int empresaId, SalvarClienteRequest request, CancellationToken ct)
    {
        var entidade = new Cliente { EmpresaId = empresaId };
        Aplicar(entidade, request);
        await AplicarFamiliaAsync(entidade, request, ct);
        await GarantirContatoLivreAsync(entidade, ct);
        entidade.DataCriacao = DateTime.UtcNow;
        db.Clientes.Add(entidade);
        await db.SaveChangesAsync(ct);
        return (await ObterAsync(empresaId, entidade.Id, ct))!;
    }

    public async Task<ClienteDto> AtualizarAsync(int empresaId, int id, SalvarClienteRequest request, CancellationToken ct)
    {
        var entidade = await db.Clientes.FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Cliente não encontrado.");
        Aplicar(entidade, request);
        await AplicarFamiliaAsync(entidade, request, ct);
        await GarantirContatoLivreAsync(entidade, ct);
        entidade.DataAtualizacao = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await ObterAsync(empresaId, id, ct))!;
    }

    public async Task<ClienteDto> AlterarAtivoAsync(int empresaId, int id, bool ativo, CancellationToken ct)
    {
        var entidade = await db.Clientes.FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Cliente não encontrado.");
        entidade.Ativo = ativo;
        entidade.DataAtualizacao = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await ObterAsync(empresaId, id, ct))!;
    }

    public async Task<IReadOnlyList<ClienteDto>> FamiliaAsync(int empresaId, int id, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Cliente não encontrado.");
        if (cliente.FamiliaId is not int familiaId)
        {
            return [];
        }
        var membros = await db.Clientes.AsNoTracking()
            .Include(x => x.Familia)
            .Where(x => x.EmpresaId == empresaId && x.FamiliaId == familiaId)
            .OrderBy(x => x.Nome)
            .ToListAsync(ct);
        return membros.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ClienteDto>> MesmoContatoAsync(
        int empresaId,
        string? telefone,
        string? email,
        int? ignorarId,
        CancellationToken ct)
    {
        var tel = SomenteDigitos(telefone);
        var mail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        if (tel.Length < 10 && mail is null)
        {
            return [];
        }
        var q = db.Clientes.AsNoTracking()
            .Include(x => x.Familia)
            .Where(x => x.EmpresaId == empresaId && ((tel.Length >= 10 && x.Telefone == tel) || (mail != null && x.Email == mail)));
        if (ignorarId is int ignorar)
        {
            q = q.Where(x => x.Id != ignorar);
        }
        var itens = await q.OrderBy(x => x.Nome).Take(20).ToListAsync(ct);
        return itens.Select(Map).ToList();
    }

    private async Task AplicarFamiliaAsync(Cliente entidade, SalvarClienteRequest request, CancellationToken ct)
    {
        if (request.SairDaFamilia)
        {
            entidade.FamiliaId = null;
            entidade.Familia = null;
            entidade.Parentesco = null;
            return;
        }
        if (request.FamiliaDeClienteId is not int outroId)
        {
            return;
        }
        if (outroId == entidade.Id)
        {
            throw new ArgumentException("Escolha outro cliente para formar a família.");
        }

        var outro = await db.Clientes.Include(x => x.Familia)
            .FirstOrDefaultAsync(x => x.Id == outroId && x.EmpresaId == entidade.EmpresaId, ct)
            ?? throw new ArgumentException("Cliente da família não encontrado.");
        if (outro.Familia is null)
        {
            outro.Familia = new Familia
            {
                EmpresaId = entidade.EmpresaId,
                Nome = NomeFamilia(outro.Nome),
                DataCriacao = DateTime.UtcNow,
            };
        }
        entidade.Familia = outro.Familia;
    }

    /// <summary>Telefone e e-mail só podem se repetir entre membros da mesma família.</summary>
    private async Task GarantirContatoLivreAsync(Cliente entidade, CancellationToken ct)
    {
        var familiaId = entidade.Familia?.Id ?? entidade.FamiliaId;
        // Família recém-criada ainda não tem Id: os membros são as fichas já ligadas a ela em memória.
        var membrosNovaFamilia = entidade.Familia is { Id: 0 } nova
            ? db.ChangeTracker.Entries<Cliente>().Where(e => e.Entity.Familia == nova).Select(e => e.Entity.Id).ToHashSet()
            : [];
        var conflitos = await db.Clientes.AsNoTracking()
            .Where(x => x.EmpresaId == entidade.EmpresaId
                && x.Id != entidade.Id
                && (x.Telefone == entidade.Telefone || (entidade.Email != null && x.Email == entidade.Email)))
            .Select(x => new { x.Id, x.Nome, x.Telefone, x.FamiliaId })
            .ToListAsync(ct);
        foreach (var c in conflitos)
        {
            if ((familiaId is > 0 && c.FamiliaId == familiaId) || membrosNovaFamilia.Contains(c.Id))
            {
                continue;
            }
            var campo = c.Telefone == entidade.Telefone ? "telefone" : "e-mail";
            throw new InvalidOperationException(
                $"{c.Nome} já usa este {campo}. Para repetir o {campo}, inclua as duas fichas na mesma família.");
        }
    }

    private static string NomeFamilia(string nome)
    {
        var partes = nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sobrenome = partes.Length > 1 ? partes[^1] : partes.FirstOrDefault() ?? "Sem nome";
        return $"Família {sobrenome}";
    }

    private static void Aplicar(Cliente entidade, SalvarClienteRequest request)
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

        var telefone = SomenteDigitos(request.Telefone);
        if (telefone.Length is < 10 or > 15)
        {
            throw new ArgumentException("Telefone deve ter entre 10 e 15 dígitos.");
        }

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        if (email is { Length: > 160 })
        {
            throw new ArgumentException("E-mail deve ter no máximo 160 caracteres.");
        }
        if (email is not null && !EmailValido(email))
        {
            throw new ArgumentException("E-mail inválido.");
        }

        var obs = string.IsNullOrWhiteSpace(request.Observacoes) ? null : request.Observacoes.Trim();
        if (obs is { Length: > 1000 })
        {
            throw new ArgumentException("Observações devem ter no máximo 1000 caracteres.");
        }

        var parentesco = string.IsNullOrWhiteSpace(request.Parentesco) ? null : request.Parentesco.Trim();
        if (parentesco is { Length: > 40 })
        {
            throw new ArgumentException("Parentesco deve ter no máximo 40 caracteres.");
        }

        entidade.Nome = nome;
        entidade.Telefone = telefone;
        entidade.Email = email;
        entidade.Observacoes = obs;
        entidade.Ativo = request.Ativo;
        entidade.Parentesco = parentesco;
    }

    private static string SomenteDigitos(string? valor) =>
        string.IsNullOrEmpty(valor) ? string.Empty : Regex.Replace(valor, @"\D", "");

    private static bool EmailValido(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && !email.Contains(' ');
    }

    private static ClienteDto Map(Cliente c) => new(
        c.Id,
        c.EmpresaId,
        c.Nome,
        c.Telefone,
        c.Email,
        c.Observacoes,
        c.Ativo,
        c.FamiliaId,
        c.Familia?.Nome,
        c.Parentesco,
        c.DataCriacao,
        c.DataAtualizacao);
}
