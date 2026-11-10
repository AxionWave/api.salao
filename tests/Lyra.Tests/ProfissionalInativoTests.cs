using Lyra.Tests.Infra;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

/// <summary>R-10, R-11 e R-22: o que acontece com a agenda e os serviços quando um profissional sai.</summary>
[Collection(ColecaoApi.Nome)]
public sealed class ProfissionalInativoTests(ApiFixture api)
{
    private static Task<Resposta> Ativo(Cenario c, int id, bool ativo) =>
        c.Admin.Patch($"/api/salao/profissionais/{id}/ativo", new { ativo });

    [Fato]
    public async Task Impacto_lista_os_futuros_que_saem_do_expediente_ou_da_agenda()
    {
        var c = await Criar(api);
        await c.AgendarOk(c.Maria, c.Bruno, [c.Retorno], c.Terca, "08:00");
        await c.AgendarOk(c.Joao, c.Bruno, [c.Retorno], c.Terca, "10:00");
        var cancelado = await c.AgendarOk(c.Joao, c.Bruno, [c.Retorno], c.Quarta, "08:00");
        Exigir(await c.Admin.Patch($"/api/salao/agendamentos/{cancelado}/status", new { status = "cancelado" }), 200);
        var comecaAs9 = Enumerable.Range(1, 6).Select(d => Faixa(d, "09:00", "17:00")).ToArray();

        var mesmo = Exigir(await c.Admin.Post($"/api/salao/profissionais/{c.Bruno}/impacto", new { ativo = true }), 200);
        var mudaHorario = Exigir(await c.Admin.Post($"/api/salao/profissionais/{c.Bruno}/impacto", new { ativo = true, disponibilidades = comecaAs9 }), 200);
        var desativar = Exigir(await c.Admin.Post($"/api/salao/profissionais/{c.Bruno}/impacto", new { ativo = false }), 200);

        Assert.Equal(0, mesmo.Json.Int("total"));
        Assert.Equal(1, mudaHorario.Json.Int("total"));
        Assert.Equal("Maria", mudaHorario.Json.GetProperty("itens")[0].Str("clienteNome"));
        Assert.Equal(2, desativar.Json.Int("total"));
        Assert.Equal(404, (await c.Admin.Post("/api/salao/profissionais/999999999/impacto", new { ativo = false })).Codigo);
    }

    [Fato]
    public async Task Inativo_continua_na_grade_com_agendamentos_mas_nao_recebe_novos()
    {
        var c = await Criar(api);
        var semAgenda = await c.NovoProfissional("Sem agenda", [Faixa(1, "08:00", "18:00")]);
        var id = await c.AgendarOk(c.Maria, c.Bruno, [c.Retorno], c.Terca, "08:00");
        Exigir(await Ativo(c, c.Bruno, false), 200);
        Exigir(await Ativo(c, semAgenda, false), 200);

        var opcoes = Exigir(await c.Admin.Get("/api/salao/agendamentos/opcoes"), 200);
        var profs = opcoes.Json.GetProperty("profissionais").EnumerateArray().ToDictionary(p => p.Int("id"), p => p.GetProperty("ativo").GetBoolean());
        var novo = await c.Agendar(c.Joao, c.Bruno, [c.Retorno], c.Quarta, "10:00");
        var confirmar = await c.Admin.Patch($"/api/salao/agendamentos/{id}/status", new { status = "confirmado" });
        var mover = await c.Admin.Put($"/api/salao/agendamentos/{id}", Corpo(c.Maria, c.Ana, [c.Retorno], Utc(c.Terca, "09:00")));

        Assert.False(profs[c.Bruno]);
        Assert.True(profs[c.Ana]);
        Assert.False(profs.ContainsKey(semAgenda));
        Assert.Equal(400, novo.Codigo);
        Assert.Contains("inativo", novo.Mensagem);
        Assert.Equal(200, confirmar.Codigo);
        Assert.Equal(200, mover.Codigo);
    }

    [Fato]
    public async Task Servico_lista_inativos_vinculados_marcados_como_inativos()
    {
        var c = await Criar(api);
        var solta = await c.NovoProfissional("Solta", [Faixa(1, "08:00", "18:00")]);
        Exigir(await Ativo(c, c.Bruno, false), 200);
        Exigir(await Ativo(c, solta, false), 200);

        var antes = Exigir(await c.Admin.Get("/api/salao/servicos?pageSize=50"), 200).Json.GetProperty("profissionais")
            .EnumerateArray().ToDictionary(p => p.Int("id"), p => p.GetProperty("ativo").GetBoolean());
        Exigir(await c.Admin.Put($"/api/salao/servicos/{c.Avaliacao}", new { nome = "Avaliação", duracaoMinutos = 60, preco = 200, ativo = true, profissionalIds = Array.Empty<int>() }), 200);
        var depois = Exigir(await c.Admin.Get("/api/salao/servicos?pageSize=50"), 200).Json.GetProperty("profissionais")
            .EnumerateArray().Select(p => p.Int("id")).ToArray();

        Assert.False(antes[c.Bruno]);
        Assert.True(antes[c.Ana]);
        Assert.False(antes.ContainsKey(solta));
        Assert.DoesNotContain(c.Bruno, depois);
    }
}
