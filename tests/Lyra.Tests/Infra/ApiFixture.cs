using Lyra.Infrastructure.Notificacoes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
        Environment.SetEnvironmentVariable("Notificacoes__Worker", "false");
        Environment.SetEnvironmentVariable("Notificacoes__Canais__0", CanalTeste.NomeCanal);
        Environment.SetEnvironmentVariable("Notificacoes__Canais__1", CanalEmail.NomeCanal);
        Environment.SetEnvironmentVariable("GATEWAY_INTERNAL_TOKEN", TokenInterno);
        Environment.SetEnvironmentVariable("Email__UrlMensageria", "http://mensageria.falsa");
        Environment.SetEnvironmentVariable("Email__UrlPublica", UrlPublica);
        Environment.SetEnvironmentVariable("Email__UrlArquivos", UrlArquivos);

        // O cadastro de empresas é do Core; aqui só as colunas que o Lyra lê.
        await ExecutarNoBanco("""
            CREATE SCHEMA core;
            CREATE TABLE core.empresas (
                id integer PRIMARY KEY, razao_social varchar(200) NOT NULL, nome_fantasia varchar(200),
                email varchar(200), telefone varchar(40), endereco varchar(200), cidade varchar(100), estado varchar(2),
                logo_token varchar(80));
            """);

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Testing")
            .ConfigureServices(s =>
            {
                s.AddSingleton<ICanalNotificacao>(Canal);
                s.AddHttpClient<CanalEmail>().ConfigurePrimaryHttpMessageHandler(() => Mensageria);
            }));
        _ = factory.Server;
    }

    public const string UrlPublica = "https://lyra.teste";

    public const string UrlArquivos = "https://gateway.teste";

    public CanalTeste Canal { get; } = new();

    public const string TokenInterno = "token-interno-dos-testes";

    public MensageriaFalsa Mensageria { get; } = new();

    public Task ExecutarNoBanco(string sql, params object[] parametros) => Executar(banco!, sql, parametros);

    public HttpClient NovoHttp() => factory?.CreateClient()
        ?? throw new InvalidOperationException("A API de testes não subiu.");

    /// <summary>Roda uma passada do processador da fila, como o worker faria.</summary>
    public async Task<int> ProcessarFila()
    {
        using var scope = Servicos.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProcessadorNotificacoes>()
            .ProcessarPendentesAsync(10_000, CancellationToken.None);
    }

    public IServiceProvider Servicos => factory?.Services
        ?? throw new InvalidOperationException("A API de testes não subiu.");

    public async Task DisposeAsync()
    {
        factory?.Dispose();
        if (servidor is null || banco is null) return;
        NpgsqlConnection.ClearAllPools();
        await ExecutarNoServidor($"DROP DATABASE IF EXISTS \"{banco}\" WITH (FORCE)");
    }

    private Task ExecutarNoServidor(string sql) => Executar("postgres", sql);

    /// <summary>Parâmetros posicionais: <c>$1</c>, <c>$2</c>…</summary>
    private async Task Executar(string nomeBanco, string sql, params object[] parametros)
    {
        var alvo = new NpgsqlConnectionStringBuilder(servidor!.ConnectionString) { Database = nomeBanco };
        await using var conexao = new NpgsqlConnection(alvo.ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        foreach (var p in parametros) comando.Parameters.Add(new NpgsqlParameter { Value = p });
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
