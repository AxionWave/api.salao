using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Lyra.Tests.Infra;

[CollectionDefinition(Nome)]
public sealed class ColecaoApi : ICollectionFixture<ApiFixture>
{
    public const string Nome = "api";
}

/// <summary>
/// Sobe a API em memória contra um banco criado só para esta execução (as migrations rodam no startup)
/// e apaga o banco no fim. O Postgres vem de <c>LYRA_TEST_DB</c>, sem o nome do banco.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string VariavelBanco = "LYRA_TEST_DB";
    internal const string Segredo = "chave-so-dos-testes-de-integracao-do-lyra-0123456789";

    private WebApplicationFactory<Program>? factory;
    private NpgsqlConnectionStringBuilder? servidor;
    private string? banco;

    public static string? ConexaoServidor => Environment.GetEnvironmentVariable(VariavelBanco);

    /// <summary>No CI os testes nunca são pulados: sem banco, falham.</summary>
    public static bool Pular =>
        string.IsNullOrWhiteSpace(ConexaoServidor) && !string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (Pular) return;
        if (string.IsNullOrWhiteSpace(ConexaoServidor))
        {
            throw new InvalidOperationException($"Defina {VariavelBanco} (ex.: Host=localhost;Port=5432;Username=postgres;Password=postgres).");
        }

        servidor = new NpgsqlConnectionStringBuilder(ConexaoServidor);
        banco = $"lyra_testes_{Guid.NewGuid():N}"[..28];
        await ExecutarNoServidor($"CREATE DATABASE \"{banco}\"");

        var conexao = new NpgsqlConnectionStringBuilder(servidor.ConnectionString) { Database = banco, SearchPath = "lyra" };
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", conexao.ConnectionString);
        Environment.SetEnvironmentVariable("JWT_SECRET", Segredo);

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        _ = factory.Server;
    }

    public HttpClient NovoHttp() => factory?.CreateClient()
        ?? throw new InvalidOperationException("A API de testes não subiu.");

    public async Task DisposeAsync()
    {
        factory?.Dispose();
        if (servidor is null || banco is null) return;
        NpgsqlConnection.ClearAllPools();
        await ExecutarNoServidor($"DROP DATABASE IF EXISTS \"{banco}\" WITH (FORCE)");
    }

    private async Task ExecutarNoServidor(string sql)
    {
        var admin = new NpgsqlConnectionStringBuilder(servidor!.ConnectionString) { Database = "postgres" };
        await using var conexao = new NpgsqlConnection(admin.ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync();
    }
}

/// <summary>Teste que precisa do Postgres: fora do CI, é pulado quando <c>LYRA_TEST_DB</c> não está definido.</summary>
public sealed class FatoAttribute : FactAttribute
{
    public FatoAttribute()
    {
        if (ApiFixture.Pular) Skip = MotivoPular;
    }

    internal static string MotivoPular => $"Defina {ApiFixture.VariavelBanco} para rodar os testes de integração.";
}

/// <inheritdoc cref="FatoAttribute"/>
public sealed class TeoriaAttribute : TheoryAttribute
{
    public TeoriaAttribute()
    {
        if (ApiFixture.Pular) Skip = FatoAttribute.MotivoPular;
    }
}
