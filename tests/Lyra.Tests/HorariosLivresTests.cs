using Lyra.Tests.Infra;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class HorariosLivresTests(ApiFixture api)
{
    [Fato]
    public async Task Respeita_ocupado_preparo_pausa_e_fim_do_expediente()
    {
        var c = await Criar(api);
        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Segunda, "10:00");
        await c.AgendarOk(c.Joao, c.Ana, [c.Consulta], c.Segunda, "13:00");

        var horas = await c.HorasLivres(c.Segunda, c.Consulta, c.Ana);

        Assert.Contains("09:00", horas);
        Assert.Contains("09:15", horas);
        Assert.DoesNotContain("09:30", horas); // o preparo encostaria nas 10:00
        Assert.DoesNotContain(horas, h => h is "10:00" or "10:15" or "10:30");
        Assert.Contains("11:00", horas);
        Assert.Contains("11:30", horas);
        Assert.DoesNotContain("11:45", horas); // cruza o almoço
        Assert.DoesNotContain(horas, h => h.StartsWith("12:"));
        Assert.DoesNotContain("13:00", horas);
        Assert.Contains("13:45", horas); // 13:00 + 30 + 10 de preparo = 13:40
        Assert.Equal("17:30", horas[^1]);
    }

    [Fato]
    public async Task So_lista_quem_atende_o_servico()
    {
        var c = await Criar(api);

        var avaliacao = Exigir(await c.Admin.Get($"/api/salao/agendamentos/horarios-livres?data={Dia(c.Segunda)}&servicoIds={c.Avaliacao}"), 200);
        var retorno = Exigir(await c.Admin.Get($"/api/salao/agendamentos/horarios-livres?data={Dia(c.Segunda)}&servicoIds={c.Retorno}"), 200);

        var soBruno = avaliacao.Json.GetProperty("profissionais").EnumerateArray().Select(p => p.Int("profissionalId")).ToArray();
        var todos = retorno.Json.GetProperty("profissionais").EnumerateArray().Select(p => p.Int("profissionalId")).ToArray();
        Assert.Equal([c.Bruno], soBruno);
        Assert.Contains(c.Ana, todos);
        Assert.Contains(c.Bruno, todos);
    }

    [Fato]
    public async Task Dia_sem_expediente_ou_no_passado_nao_tem_horarios()
    {
        var c = await Criar(api);
        var passado = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);

        Assert.Empty(await c.HorasLivres(c.Sabado, c.Retorno, c.Ana));
        Assert.Empty(await c.HorasLivres(passado, c.Retorno, c.Bruno));
    }

    [Fato]
    public async Task Ao_remarcar_o_proprio_horario_aparece_livre()
    {
        var c = await Criar(api);
        var id = await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Segunda, "10:00");

        Assert.DoesNotContain("10:00", await c.HorasLivres(c.Segunda, c.Consulta, c.Ana));
        Assert.Contains("10:00", await c.HorasLivres(c.Segunda, c.Consulta, c.Ana, ignorar: id));
    }

    [Fato]
    public async Task Fuso_da_empresa_define_a_hora_local()
    {
        var c = await Criar(api);
        Exigir(await c.Admin.Put("/api/salao/configuracao", new { segmento = "saude", fusoHorario = "America/Manaus", terminologia = (object?)null }), 200);

        var r = Exigir(await c.Admin.Get($"/api/salao/agendamentos/horarios-livres?data={Dia(c.Quarta)}&servicoIds={c.Retorno}&profissionalId={c.Bruno}"), 200);
        var primeiro = r.Json.GetProperty("profissionais")[0].GetProperty("horarios")[0];
        var antes = await c.Admin.Post("/api/salao/agendamentos", Corpo(c.Maria, c.Bruno, [c.Retorno], Utc(c.Quarta, "07:30", offsetHoras: -4)));

        Assert.Equal("08:00", primeiro.Str("hora"));
        Assert.Equal(12, primeiro.GetProperty("inicio").GetDateTime().ToUniversalTime().Hour);
        Assert.Equal(409, antes.Codigo);
    }
}
