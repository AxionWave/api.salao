using Lyra.Tests.Infra;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class BloqueioTests(ApiFixture api)
{
    private static async Task<int> Bloquear(Cenario c, int? profissional, string inicio, string fim, string? motivo)
    {
        var r = Exigir(await c.Admin.Post("/api/salao/bloqueios", new { profissionalId = profissional, inicio, fim, motivo }), 201);
        return r.Json.GetProperty("bloqueio").Int("id");
    }

    [Fato]
    public async Task Bloqueio_do_profissional_vale_so_para_ele_e_zera_os_horarios()
    {
        var c = await Criar(api);
        await Bloquear(c, c.Ana, Utc(c.Terca, "00:00"), Utc(c.Quarta, "00:00"), "Férias");

        var ana = await c.Agendar(c.Maria, c.Ana, [c.Retorno], c.Terca, "10:00");
        var bruno = await c.Agendar(c.Maria, c.Bruno, [c.Retorno], c.Terca, "10:00");

        Assert.Equal(409, ana.Codigo);
        Assert.Contains("Férias", ana.Mensagem);
        Assert.Equal(201, bruno.Codigo);
        Assert.Empty(await c.HorasLivres(c.Terca, c.Retorno, c.Ana));
    }

    [Fato]
    public async Task Bloqueio_da_empresa_vale_para_todos_ate_o_fim()
    {
        var c = await Criar(api);
        await Bloquear(c, null, Utc(c.Quarta, "14:00"), Utc(c.Quarta, "15:00"), "Reunião geral");

        var dentro = await c.Agendar(c.Maria, c.Bruno, [c.Retorno], c.Quarta, "14:30");
        var depois = await c.Agendar(c.Maria, c.Bruno, [c.Retorno], c.Quarta, "15:00");

        Assert.Equal(409, dentro.Codigo);
        Assert.Contains("Reunião geral", dentro.Mensagem);
        Assert.Equal(201, depois.Codigo);
    }

    [Fato]
    public async Task Fim_antes_do_inicio_e_recusado()
    {
        var c = await Criar(api);
        var r = await c.Admin.Post("/api/salao/bloqueios", new { profissionalId = (int?)null, inicio = Utc(c.Quarta, "15:00"), fim = Utc(c.Quarta, "14:00") });
        Assert.Equal(400, r.Codigo);
    }

    [Fato]
    public async Task Aviso_conta_so_agendamentos_em_aberto_do_periodo()
    {
        var c = await Criar(api);
        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Segunda, "09:00");
        await c.AgendarOk(c.Joao, c.Ana, [c.Consulta], c.Segunda, "10:00");
        var cancelado = await c.AgendarOk(c.Joao, c.Ana, [c.Retorno], c.Segunda, "11:00");
        Exigir(await c.Admin.Patch($"/api/salao/agendamentos/{cancelado}/status", new { status = "cancelado" }), 200);

        var r = Exigir(await c.Admin.Post("/api/salao/bloqueios", new { profissionalId = c.Ana, inicio = Utc(c.Segunda, "08:00"), fim = Utc(c.Segunda, "12:00"), motivo = "Congresso" }), 201);

        Assert.Equal(2, r.Json.Int("agendamentosNoPeriodo"));
    }

    [Fato]
    public async Task Listar_filtra_por_profissional_e_remover_libera_o_horario()
    {
        var c = await Criar(api);
        var ferias = await Bloquear(c, c.Ana, Utc(c.Terca, "00:00"), Utc(c.Quarta, "00:00"), "Férias");
        var reuniao = await Bloquear(c, null, Utc(c.Quarta, "14:00"), Utc(c.Quarta, "15:00"), "Reunião");
        var periodo = $"de={Utc(c.Segunda, "00:00")}&ate={Utc(c.Sabado, "00:00")}";

        var todos = Exigir(await c.Admin.Get($"/api/salao/bloqueios?{periodo}"), 200);
        var doBruno = Exigir(await c.Admin.Get($"/api/salao/bloqueios?{periodo}&profissionalId={c.Bruno}"), 200);
        var ids = todos.Json.GetProperty("items").EnumerateArray().Select(b => b.Int("id")).ToArray();
        var idsBruno = doBruno.Json.GetProperty("items").EnumerateArray().Select(b => b.Int("id")).ToArray();

        Assert.Contains(ferias, ids);
        Assert.Contains(reuniao, ids);
        Assert.Equal([reuniao], idsBruno);

        Assert.Equal(204, (await c.Admin.Delete($"/api/salao/bloqueios/{ferias}")).Codigo);
        Assert.Equal(201, (await c.Agendar(c.Joao, c.Ana, [c.Retorno], c.Terca, "10:00")).Codigo);
        Assert.Equal(404, (await c.Admin.Delete($"/api/salao/bloqueios/{ferias}")).Codigo);
    }
}
