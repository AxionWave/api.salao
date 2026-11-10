using Lyra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>Como a empresa aparece para o cliente nas mensagens.</summary>
public sealed record EmpresaRemetente(string Nome, string? Email, string? Telefone, string? Endereco);

/// <summary>Lê o cadastro da empresa no Core (mesmo banco, schema <c>core</c>), só leitura.</summary>
public sealed class LeitorEmpresa(LyraDbContext db, ILogger<LeitorEmpresa> logger)
{
    private sealed record Linha(string? NomeFantasia, string? RazaoSocial, string? Email, string? Telefone,
        string? Endereco, string? Cidade, string? Estado);

    public async Task<EmpresaRemetente?> ObterAsync(int empresaId, CancellationToken ct)
    {
        Linha? linha;
        try
        {
            linha = await db.Database
                .SqlQueryRaw<Linha>(
                    """
                    SELECT nome_fantasia AS "NomeFantasia", razao_social AS "RazaoSocial", email AS "Email",
                           telefone AS "Telefone", endereco AS "Endereco", cidade AS "Cidade", estado AS "Estado"
                    FROM core.empresas
                    WHERE id = {0}
                    """,
                    empresaId)
                .FirstOrDefaultAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName)
        {
            logger.LogWarning("core.empresas não existe neste banco; mensagens saem sem os dados da empresa");
            return null;
        }
        if (linha is null) return null;

        var nome = Texto(linha.NomeFantasia) ?? Texto(linha.RazaoSocial);
        if (nome is null) return null;
        var cidade = string.Join(" - ", new[] { Texto(linha.Cidade), Texto(linha.Estado) }.Where(p => p is not null));
        var endereco = string.Join(", ", new[] { Texto(linha.Endereco), Texto(cidade) }.Where(p => p is not null));
        return new EmpresaRemetente(nome, Texto(linha.Email), Texto(linha.Telefone), Texto(endereco));
    }

    private static string? Texto(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
