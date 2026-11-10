using Lyra.Tests.Infra;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class StatusTests(ApiFixture api)
{
    private static Task<Resposta> Status(Cenario c, int id, string status, string? motivo = null) =>
        c.Admin.Patch($"/api/salao/agendamentos/{id}/status", new { status, motivo });

    [Fato]
    public async Task Futuro_pode_ser_confirmado_mas_nao_concluido_nem_falta()
    {
        var c = await Criar(api);
        var id = await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Segunda, "10:00");

        Assert.Equal(200, (await Status(c, id, "confirmado")).Codigo);
        Assert.Equal(409, (await Status(c, id, "concluido")).Codigo);
        Assert.Equal(409, (await Status(c, id, "no_show")).Codigo);
        Assert.Equal(400, (await Status(c, id, "inexistente")).Codigo);
    }

    [Fato]
    public async Task Cancelar_libera_o_horario_e_trava_a_edicao()
    {
        var c = await Criar(api);
        var id = await c.AgendarOk(c.Joao, c.Ana, [c.Retorno], c.Segunda, "10:40");

        Assert.Equal(200, (await Status(c, id, "cancelado")).Codigo);
        var editar = await c.Admin.Put($"/api/salao/agendamentos/{id}", Corpo(c.Joao, c.Ana, [c.Retorno], Utc(c.Segunda, "10:40"), "x"));
        var voltar = await Status(c, id, "agendado");
        var outro = await c.Agendar(c.Maria, c.Ana, [c.Retorno], c.Segunda, "10:40");

        Assert.Equal(409, editar.Codigo);
        Assert.Equal(409, voltar.Codigo);
        Assert.Equal(201, outro.Codigo);
    }

    [Fato]
    public async Task Motivo_do_cancelamento_e_aparado_limitado_e_so_vale_ao_cancelar()
    {
        var c = await Criar(api);
        var a = await c.AgendarOk(c.Maria, c.Bruno, [c.Retorno], c.Terca, "09:00");
        var b = await c.AgendarOk(c.Maria, c.Bruno, [c.Retorno], c.Terca, "10:00");
        var d = await c.AgendarOk(c.Maria, c.Bruno, [c.Retorno], c.Terca, "11:00");

        var aparado = await Status(c, a, "cancelado", "  Cliente viajou  ");
        var longo = await Status(c, b, "cancelado", new string('x', 201));
        var confirmado = await Status(c, d, "confirmado", "ignorado");

        Assert.Equal("Cliente viajou", aparado.Json.Str("motivoCancelamento"));
        Assert.Equal(400, longo.Codigo);
        Assert.True(confirmado.Json.Nulo("motivoCancelamento"));
    }

    [Fato]
    public async Task Atendimento_perto_do_horario_e_falta_depois_do_inicio()
    {
        var c = await Criar(api);
        var plantao = await c.NovoProfissional("Plantão", SemanaInteira());
        var agora = DateTime.UtcNow;
        var inicioAgora = agora.AddSeconds(-agora.Second).ToString("yyyy-MM-ddTHH:mm:00Z");
        var daqui50 = agora.AddMinutes(50).ToString("yyyy-MM-ddTHH:mm:00Z");
        var daqui3h = agora.AddHours(3).ToString("yyyy-MM-ddTHH:mm:00Z");

        var falta = Exigir(await c.Admin.Post("/api/salao/agendamentos", Corpo(c.Maria, plantao, [c.Retorno], inicioAgora)), 201).Id;
        var perto = Exigir(await c.Admin.Post("/api/salao/agendamentos", Corpo(c.Joao, plantao, [c.Retorno], daqui50)), 201).Id;
        var longe = Exigir(await c.Admin.Post("/api/salao/agendamentos", Corpo(c.Joao, plantao, [c.Retorno], daqui3h)), 201).Id;

        Assert.Equal(200, (await Status(c, falta, "no_show")).Codigo);
        Assert.Equal(200, (await Status(c, perto, "em_atendimento")).Codigo);
        Assert.Equal(409, (await Status(c, longe, "em_atendimento")).Codigo);
    }
}
