using System.Globalization;
using System.Text.Json;

namespace Lyra.Tests.Infra;

/// <summary>
/// Uma empresa nova por teste, com o mesmo elenco do e2e antigo:
/// Ana (seg–sex, 09–12 e 13–18), Bruno (seg–sáb, 08–17),
/// Consulta (30 min, preparo 10, só Ana), Retorno (15 min, todos), Avaliação (60 min, só Bruno),
/// e os clientes Maria e João. Fuso padrão: America/Sao_Paulo (UTC−3, sem horário de verão).
/// </summary>
public sealed class Cenario
{
    private static int proximaEmpresa = 500_000 + Random.Shared.Next(0, 100_000) * 10;
    private static int proximaPessoa = 800_000_000 + Random.Shared.Next(0, 1_000_000) * 10;

    public required int EmpresaId { get; init; }
    public required Api Admin { get; init; }
    public required Api Recepcao { get; init; }
    public required HttpClient Http { get; init; }

    public int Ana { get; private set; }
    public int Bruno { get; private set; }
    public int Consulta { get; private set; }
    public int Retorno { get; private set; }
    public int Avaliacao { get; private set; }
    public int Maria { get; private set; }
    public int Joao { get; private set; }

    /// <summary>Segunda-feira daqui a 7 a 13 dias: sempre no futuro e sem cruzar com hoje.</summary>
    public DateOnly Segunda { get; } = ProximaSegunda();
    public DateOnly Terca => Segunda.AddDays(1);
    public DateOnly Quarta => Segunda.AddDays(2);
    public DateOnly Quinta => Segunda.AddDays(3);
    public DateOnly Sabado => Segunda.AddDays(5);

    public static async Task<Cenario> Criar(ApiFixture fixture)
    {
        var empresa = Interlocked.Increment(ref proximaEmpresa);
        var http = fixture.NovoHttp();
        var c = new Cenario
        {
            EmpresaId = empresa,
            Http = http,
            Admin = new Api(http, Tokens.Gerar(empresa, Tokens.Todos)),
            Recepcao = new Api(http, Tokens.Gerar(empresa, Tokens.Recepcao)),
        };

        var comPausa = Enumerable.Range(1, 5)
            .SelectMany(d => new[] { Faixa(d, "09:00", "12:00"), Faixa(d, "13:00", "18:00") })
            .ToArray();
        c.Ana = await c.NovoProfissional("Ana", comPausa);
        c.Bruno = await c.NovoProfissional("Bruno", Enumerable.Range(1, 6).Select(d => Faixa(d, "08:00", "17:00")).ToArray());
        c.Consulta = await c.NovoServico("Consulta", 30, 150, preparo: 10, profissionais: [c.Ana]);
        c.Retorno = await c.NovoServico("Retorno", 15, 0);
        c.Avaliacao = await c.NovoServico("Avaliação", 60, 200, profissionais: [c.Bruno]);
        c.Maria = await c.NovoCliente("Maria");
        c.Joao = await c.NovoCliente("João");
        return c;
    }

    public static object Faixa(int diaSemana, string inicio, string fim) => new { diaSemana, inicio, fim };

    public static object[] SemanaInteira(string inicio = "00:00", string fim = "23:55") =>
        Enumerable.Range(0, 7).Select(d => Faixa(d, inicio, fim)).ToArray();

    public async Task<int> NovoProfissional(string nome, object[] faixas)
    {
        var r = await Admin.Post("/api/salao/profissionais", new
        {
            nome,
            pessoaId = Interlocked.Increment(ref proximaPessoa),
            ativo = true,
            disponibilidades = faixas,
        });
        return Exigir(r, 201).Id;
    }

    public async Task<int> NovoServico(string nome, int duracao, decimal preco, int preparo = 0, int[]? profissionais = null)
    {
        var r = await Admin.Post("/api/salao/servicos", new
        {
            nome,
            duracaoMinutos = duracao,
            preco,
            ativo = true,
            intervaloAposMinutos = preparo,
            profissionalIds = profissionais ?? [],
        });
        return Exigir(r, 201).Id;
    }

    public async Task<int> NovoCliente(string nome)
    {
        var telefone = "119" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
        var r = await Admin.Post("/api/salao/clientes", new { nome, telefone, ativo = true });
        return Exigir(r, 201).Id;
    }

    public static object Corpo(int cliente, int profissional, int[] servicos, string inicio, string? observacoes = null) =>
        new { clienteId = cliente, profissionalId = profissional, servicoIds = servicos, inicio, observacoes };

    public Task<Resposta> Agendar(int cliente, int profissional, int[] servicos, DateOnly dia, string hora) =>
        Admin.Post("/api/salao/agendamentos", Corpo(cliente, profissional, servicos, Utc(dia, hora)));

    public async Task<int> AgendarOk(int cliente, int profissional, int[] servicos, DateOnly dia, string hora) =>
        Exigir(await Agendar(cliente, profissional, servicos, dia, hora), 201).Id;

    public async Task<string[]> HorasLivres(DateOnly dia, int servico, int profissional, int? ignorar = null)
    {
        var rota = $"/api/salao/agendamentos/horarios-livres?data={Dia(dia)}&servicoIds={servico}&profissionalId={profissional}";
        if (ignorar is int id) rota += $"&ignorarAgendamentoId={id}";
        var r = Exigir(await Admin.Get(rota), 200);
        var profs = r.Json.GetProperty("profissionais");
        return profs.GetArrayLength() == 0
            ? []
            : profs[0].GetProperty("horarios").EnumerateArray().Select(h => h.GetProperty("hora").GetString()!).ToArray();
    }

    /// <summary>Hora local da empresa (UTC−3 por padrão) em ISO UTC.</summary>
    public static string Utc(DateOnly dia, string hora, int offsetHoras = -3) =>
        dia.ToDateTime(TimeOnly.Parse(hora, CultureInfo.InvariantCulture), DateTimeKind.Utc)
            .AddHours(-offsetHoras)
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static string Dia(DateOnly dia) => dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Resposta Exigir(Resposta r, int codigo)
    {
        Assert.True(r.Codigo == codigo, $"Esperado {codigo}, veio {r}");
        return r;
    }

    private static DateOnly ProximaSegunda()
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        var ate = ((int)DayOfWeek.Monday - (int)hoje.DayOfWeek + 7) % 7;
        return hoje.AddDays(ate + 7);
    }
}

public static class JsonExt
{
    public static string Str(this JsonElement e, string nome) => e.GetProperty(nome).GetString() ?? "";
    public static int Int(this JsonElement e, string nome) => e.GetProperty(nome).GetInt32();
    public static bool Nulo(this JsonElement e, string nome) =>
        !e.TryGetProperty(nome, out var v) || v.ValueKind == JsonValueKind.Null;
}
