using Lyra.Tests.Infra;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

/// <summary>Conflito, preparo, expediente com pausa, quem atende e serviços somados.</summary>
[Collection(ColecaoApi.Nome)]
public sealed class AgendamentoRegrasTests(ApiFixture api)
{
    [Fato]
    public async Task Mesmo_horario_do_mesmo_profissional_e_recusado()
    {
        var c = await Criar(api);
        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Segunda, "10:00");

        var r = await c.Agendar(c.Joao, c.Ana, [c.Consulta], c.Segunda, "10:00");

        Assert.Equal(409, r.Codigo);
        Assert.Contains("já tem outro agendamento", r.Mensagem);
    }

    [Fato]
    public async Task Grava_fim_e_preparo_do_servico()
    {
        var c = await Criar(api);

        var r = Exigir(await c.Agendar(c.Maria, c.Ana, [c.Consulta], c.Segunda, "10:00"), 201);

        Assert.Equal(10, r.Json.Int("intervaloAposMinutos"));
        Assert.Equal(Utc(c.Segunda, "10:30"), r.Json.GetProperty("fim").GetDateTime().ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    [Fato]
    public async Task Preparo_bloqueia_o_horario_colado_e_libera_depois()
    {
        var c = await Criar(api);
        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Segunda, "10:00");

        var colado = await c.Agendar(c.Joao, c.Ana, [c.Retorno], c.Segunda, "10:30");
        var depois = await c.Agendar(c.Joao, c.Ana, [c.Retorno], c.Segunda, "10:40");

        Assert.Equal(409, colado.Codigo);
        Assert.Contains("preparo", colado.Mensagem);
        Assert.Equal(201, depois.Codigo);
    }

    [Teoria]
    [InlineData("11:45", "atravessa o almoço")]
    [InlineData("12:15", "dentro da pausa")]
    [InlineData("08:30", "antes do expediente")]
    [InlineData("17:45", "termina depois do expediente")]
    public async Task Fora_do_expediente_e_recusado(string hora, string caso)
    {
        var c = await Criar(api);

        var r = await c.Agendar(c.Joao, c.Ana, [c.Consulta], c.Segunda, hora);

        Assert.True(r.Codigo == 409 && r.Mensagem.Contains("Fora do expediente"), $"{caso}: {r}");
    }

    [Fato]
    public async Task Logo_depois_da_pausa_e_aceito()
    {
        var c = await Criar(api);
        Assert.Equal(201, (await c.Agendar(c.Joao, c.Ana, [c.Consulta], c.Segunda, "13:00")).Codigo);
    }

    [Fato]
    public async Task Profissional_que_nao_atende_o_servico_e_recusado()
    {
        var c = await Criar(api);

        var r = await c.Agendar(c.Joao, c.Ana, [c.Avaliacao], c.Segunda, "14:00");

        Assert.Equal(409, r.Codigo);
        Assert.Contains("não atende", r.Mensagem);
    }

    [Fato]
    public async Task Dia_sem_expediente_e_recusado()
    {
        var c = await Criar(api);

        var ana = await c.Agendar(c.Joao, c.Ana, [c.Retorno], c.Sabado, "10:00");
        var bruno = await c.Agendar(c.Joao, c.Bruno, [c.Avaliacao], c.Sabado, "10:00");

        Assert.Equal(409, ana.Codigo);
        Assert.Contains("não atende neste dia", ana.Mensagem);
        Assert.Equal(201, bruno.Codigo);
    }

    [Fato]
    public async Task Varios_servicos_somam_duracao_e_preco()
    {
        var c = await Criar(api);

        var passa = await c.Agendar(c.Joao, c.Bruno, [c.Retorno, c.Avaliacao], c.Segunda, "16:00");
        var cabe = Exigir(await c.Agendar(c.Joao, c.Bruno, [c.Retorno, c.Avaliacao], c.Segunda, "15:30"), 201);

        Assert.Equal(409, passa.Codigo);
        Assert.Equal(75, cabe.Json.Int("duracaoMinutos"));
        Assert.Equal(200m, cabe.Json.GetProperty("preco").GetDecimal());
    }

    [Fato]
    public async Task Marcar_no_passado_e_recusado()
    {
        var c = await Criar(api);
        var semanaPassada = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);

        var r = await c.Agendar(c.Maria, c.Bruno, [c.Retorno], semanaPassada, "10:00");

        Assert.Equal(400, r.Codigo);
    }

    [Fato]
    public async Task Editar_so_a_observacao_e_mover_sobre_o_proprio_horario()
    {
        var c = await Criar(api);
        var id = await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Segunda, "10:00");

        var obs = await c.Admin.Put($"/api/salao/agendamentos/{id}", Corpo(c.Maria, c.Ana, [c.Consulta], Utc(c.Segunda, "10:00"), "Trazer exames"));
        var mover = await c.Admin.Put($"/api/salao/agendamentos/{id}", Corpo(c.Maria, c.Ana, [c.Consulta], Utc(c.Segunda, "10:15")));
        var trocar = await c.Admin.Put($"/api/salao/agendamentos/{id}", Corpo(c.Maria, c.Ana, [c.Avaliacao], Utc(c.Segunda, "10:15")));

        Assert.Equal(200, obs.Codigo);
        Assert.Equal("Trazer exames", obs.Json.Str("observacoes"));
        Assert.Equal(200, mover.Codigo);
        Assert.Equal(409, trocar.Codigo);
    }

    [Fato]
    public async Task Servico_desativado_mantem_o_agendamento_editavel_com_preco_gravado()
    {
        var c = await Criar(api);
        var ag = Exigir(await c.Agendar(c.Joao, c.Ana, [c.Consulta], c.Segunda, "13:00"), 201);
        Exigir(await c.Admin.Patch($"/api/salao/servicos/{c.Consulta}/ativo", new { ativo = false }), 200);

        var obs = await c.Admin.Put($"/api/salao/agendamentos/{ag.Id}", Corpo(c.Joao, c.Ana, [c.Consulta], Utc(c.Segunda, "13:00"), "só a observação"));
        var trocar = await c.Admin.Put($"/api/salao/agendamentos/{ag.Id}", Corpo(c.Joao, c.Ana, [c.Consulta, c.Retorno], Utc(c.Segunda, "13:00")));

        Assert.Equal(200, obs.Codigo);
        Assert.Equal(ag.Json.GetProperty("preco").GetDecimal(), obs.Json.GetProperty("preco").GetDecimal());
        Assert.Equal(400, trocar.Codigo);
        Assert.Contains("inativo", trocar.Mensagem);
    }
}
