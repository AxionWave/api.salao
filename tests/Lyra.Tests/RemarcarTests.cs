using Lyra.Tests.Infra;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class RemarcarTests(ApiFixture api)
{
    private static Task<Resposta> Remarcar(Cenario c, int id, int cliente, int profissional, int servico, DateOnly dia, string hora) =>
        c.Admin.Post($"/api/salao/agendamentos/{id}/remarcar", Corpo(cliente, profissional, [servico], Utc(dia, hora)));

    [Fato]
    public async Task Remarcar_cria_novo_e_cancela_o_original_com_motivo()
    {
        var c = await Criar(api);
        var original = await c.AgendarOk(c.Maria, c.Bruno, [c.Retorno], c.Terca, "08:00");

        var novo = Exigir(await Remarcar(c, original, c.Maria, c.Bruno, c.Retorno, c.Terca, "08:10"), 201);
        var antigo = Exigir(await c.Admin.Get($"/api/salao/agendamentos/{original}"), 200);
        var deNovo = await Remarcar(c, original, c.Maria, c.Bruno, c.Retorno, c.Terca, "12:00");
        var cadeia = Exigir(await Remarcar(c, novo.Id, c.Joao, c.Bruno, c.Retorno, c.Terca, "13:00"), 201);

        Assert.Equal(original, novo.Json.Int("remarcadoDeId"));
        Assert.Equal("agendado", novo.Json.Str("status"));
        Assert.Equal("cancelado", antigo.Json.Str("status"));
        Assert.Equal("Remarcado", antigo.Json.Str("motivoCancelamento"));
        Assert.Equal(409, deNovo.Codigo);
        Assert.Equal(novo.Id, cadeia.Json.Int("remarcadoDeId"));
        Assert.Equal(c.Maria, cadeia.Json.Int("clienteId")); // o cliente não muda ao remarcar
    }

    [Fato]
    public async Task Remarcacao_recusada_mantem_o_original()
    {
        var c = await Criar(api);
        await c.AgendarOk(c.Joao, c.Ana, [c.Consulta], c.Terca, "13:30");
        var id = await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");
        var passado = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);

        Assert.Equal(409, (await Remarcar(c, id, c.Maria, c.Ana, c.Consulta, c.Terca, "13:30")).Codigo);
        Assert.Equal(400, (await Remarcar(c, id, c.Maria, c.Ana, c.Consulta, passado, "10:00")).Codigo);
        Assert.Equal(409, (await Remarcar(c, id, c.Maria, c.Bruno, c.Consulta, c.Terca, "15:00")).Codigo);
        Assert.Equal(409, (await Remarcar(c, id, c.Maria, c.Ana, c.Consulta, c.Terca, "20:00")).Codigo);
        Assert.Equal(404, (await Remarcar(c, 999_999_999, c.Maria, c.Ana, c.Consulta, c.Terca, "15:00")).Codigo);
        Assert.Equal("agendado", Exigir(await c.Admin.Get($"/api/salao/agendamentos/{id}"), 200).Json.Str("status"));
    }

    [Fato]
    public async Task Remarcar_trocando_profissional_e_servico_aplica_o_preco_novo()
    {
        var c = await Criar(api);
        var id = await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");

        var r = Exigir(await Remarcar(c, id, c.Maria, c.Bruno, c.Avaliacao, c.Terca, "15:00"), 201);

        Assert.Equal(200m, r.Json.GetProperty("preco").GetDecimal());
        Assert.Equal(c.Bruno, r.Json.Int("profissionalId"));
    }
}
