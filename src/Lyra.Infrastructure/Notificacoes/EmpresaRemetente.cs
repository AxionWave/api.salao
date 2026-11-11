using System.Text.RegularExpressions;
using Lyra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>Como a empresa aparece para o cliente nas mensagens.</summary>
public sealed record EmpresaRemetente(string Nome, string? Email, string? Telefone, string? Endereco, string? LogoUrl = null);

/// <summary>Lê o cadastro da empresa no Core (mesmo banco, schema <c>core</c>), só leitura.</summary>
public sealed partial class LeitorEmpresa(LyraDbContext db, IOptions<EmailOptions> opcoes, ILogger<LeitorEmpresa> logger)
{
    private sealed record Linha(string? NomeFantasia, string? RazaoSocial, string? Email, string? Telefone,
        string? Endereco, string? Cidade, string? Estado, string? LogoToken);

    public async Task<EmpresaRemetente?> ObterAsync(int empresaId, CancellationToken ct)
    {
        Linha? linha;
        try
        {
            linha = await Consultar(empresaId, comLogo: true, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName)
        {
            logger.LogWarning("core.empresas não existe neste banco; mensagens saem sem os dados da empresa");
            return null;
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedColumn)
        {
            // Core ainda sem a coluna do logo: segue sem ele.
            linha = await Consultar(empresaId, comLogo: false, ct);
        }
        if (linha is null) return null;

        var nome = Texto(linha.NomeFantasia) ?? Texto(linha.RazaoSocial);
        if (nome is null) return null;
        var cidade = string.Join(" - ", new[] { Texto(linha.Cidade), Texto(linha.Estado) }.Where(p => p is not null));
        var endereco = string.Join(", ", new[] { Texto(linha.Endereco), Texto(cidade) }.Where(p => p is not null));
        return new EmpresaRemetente(nome, Texto(linha.Email), Texto(linha.Telefone), Texto(endereco), LogoUrl(linha.LogoToken));
    }

    private const string Colunas = """
        SELECT nome_fantasia AS "NomeFantasia", razao_social AS "RazaoSocial", email AS "Email",
               telefone AS "Telefone", endereco AS "Endereco", cidade AS "Cidade", estado AS "Estado",
        """;

    private const string SqlComLogo = Colunas + """ logo_token AS "LogoToken" FROM core.empresas WHERE id = {0}""";

    private const string SqlSemLogo = Colunas + """ NULL::varchar AS "LogoToken" FROM core.empresas WHERE id = {0}""";

    private Task<Linha?> Consultar(int empresaId, bool comLogo, CancellationToken ct) =>
        db.Database.SqlQueryRaw<Linha>(comLogo ? SqlComLogo : SqlSemLogo, empresaId).FirstOrDefaultAsync(ct);

    private string? LogoUrl(string? token)
    {
        var baseUrl = opcoes.Value.UrlArquivos.TrimEnd('/');
        if (token is null || baseUrl.Length == 0 || !TokenArquivo().IsMatch(token)) return null;
        return $"{baseUrl}/api/arquivos/publico/{token}";
    }

    [GeneratedRegex("^arq_[A-Za-z0-9_-]{16,72}$")]
    private static partial Regex TokenArquivo();

    private static string? Texto(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
