using System.Text.RegularExpressions;
using Lyra.Application.Abstractions;
using Lyra.Application.Clientes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;

        var q = db.Clientes.AsNoTracking().Where(c => c.EmpresaId == empresaId);
        if (ativo is bool flag)
        {
            q = q.Where(c => c.Ativo == flag);
        }
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            var digitos = SomenteDigitos(termo);
            q = q.Where(c =>
                c.Nome.ToLower().Contains(termo)
                || (c.Email != null && c.Email.ToLower().Contains(termo))
                || (digitos.Length > 0 && c.Telefone.Contains(digitos)));
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
            .FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct);
        return c is null ? null : Map(c);
    }

    public async Task<ClienteDto> CriarAsync(int empresaId, SalvarClienteRequest request, CancellationToken ct)
    {
        var entidade = new Cliente { EmpresaId = empresaId };
        Aplicar(entidade, request);
        entidade.DataCriacao = DateTime.UtcNow;
        db.Clientes.Add(entidade);
        await SalvarAsync(ct);
        return Map(entidade);
    }

    public async Task<ClienteDto> AtualizarAsync(int empresaId, int id, SalvarClienteRequest request, CancellationToken ct)
    {
        var entidade = await db.Clientes.FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Cliente não encontrado.");
        Aplicar(entidade, request);
        entidade.DataAtualizacao = DateTime.UtcNow;
        await SalvarAsync(ct);
        return Map(entidade);
    }

    public async Task<ClienteDto> AlterarAtivoAsync(int empresaId, int id, bool ativo, CancellationToken ct)
    {
        var entidade = await db.Clientes.FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Cliente não encontrado.");
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
            if (constraint.Contains("email", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Já existe um cliente com este e-mail nesta empresa.");
            }
            throw new InvalidOperationException("Já existe um cliente com este telefone nesta empresa.");
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

        entidade.Nome = nome;
        entidade.Telefone = telefone;
        entidade.Email = email;
        entidade.Observacoes = obs;
        entidade.Ativo = request.Ativo;
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
        c.DataCriacao,
        c.DataAtualizacao);
}
