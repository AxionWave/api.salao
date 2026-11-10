using Lyra.Core.Modules;
using Lyra.Tests.Infra;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class PermissoesTests(ApiFixture api)
{
    [Fato]
    public async Task Token_com_outra_chave_ou_sem_token_recebe_401()
    {
        var c = await Criar(api);
        var forjado = new Api(c.Http, Tokens.Gerar(c.EmpresaId, Tokens.Todos, segredo: "uma-chave-qualquer-que-nao-e-a-da-api-0123456789"));
        var semToken = await c.Http.GetAsync("/api/salao/agendamentos/opcoes");

        Assert.Equal(401, (await forjado.Get("/api/salao/agendamentos/opcoes")).Codigo);
        Assert.Equal(401, (int)semToken.StatusCode);
    }

    [Fato]
    public async Task Empresa_vem_so_do_token_nunca_do_cabecalho()
    {
        var c = await Criar(api);
        var semEmpresa = new Api(c.Http, Tokens.Gerar(null, Tokens.Todos));
        using var pedido = new HttpRequestMessage(HttpMethod.Get, "/api/salao/agendamentos/opcoes");
        pedido.Headers.Authorization = new("Bearer", Tokens.Gerar(null, Tokens.Todos));
        pedido.Headers.Add("X-Empresa-Id", c.EmpresaId.ToString());

        Assert.Equal(403, (int)(await c.Http.SendAsync(pedido)).StatusCode);
        Assert.Equal(403, (await semEmpresa.Get("/api/salao/servicos")).Codigo);
    }

    [Fato]
    public async Task Dados_de_outra_empresa_nao_aparecem()
    {
        var a = await Criar(api);
        var b = await Criar(api);
        var agA = await a.AgendarOk(a.Maria, a.Ana, [a.Consulta], a.Segunda, "10:00");

        Assert.Equal(404, (await b.Admin.Get($"/api/salao/agendamentos/{agA}")).Codigo);
        Assert.Equal(400, (await b.Agendar(a.Maria, a.Ana, [a.Consulta], b.Segunda, "11:00")).Codigo);
        Assert.Equal(400, (await b.Admin.Post("/api/salao/servicos", new { nome = "Outro dono", duracaoMinutos = 30, preco = 1, ativo = true, profissionalIds = new[] { a.Ana } })).Codigo);
    }

    [Fato]
    public async Task Recepcao_agenda_mas_nao_abre_cadastros()
    {
        var c = await Criar(api);

        Assert.Equal(200, (await c.Recepcao.Get("/api/salao/agendamentos/opcoes")).Codigo);
        Assert.Equal(200, (await c.Recepcao.Get("/api/salao/agendamentos/clientes?busca=Maria")).Codigo);
        Assert.Equal(201, (await c.Recepcao.Post("/api/salao/bloqueios", new { profissionalId = (int?)null, inicio = Utc(c.Sabado, "20:00"), fim = Utc(c.Sabado, "21:00"), motivo = "recepção" })).Codigo);
        Assert.Equal(403, (await c.Recepcao.Get("/api/salao/servicos")).Codigo);
        Assert.Equal(403, (await c.Recepcao.Get("/api/salao/clientes")).Codigo);
        Assert.Equal(403, (await c.Recepcao.Get("/api/salao/configuracao/opcoes")).Codigo);
        Assert.Equal(403, (await c.Recepcao.Post($"/api/salao/profissionais/{c.Ana}/impacto", new { ativo = false })).Codigo);
    }

    [Fato]
    public async Task Perfis_de_acesso_exigem_profissionais_ou_configuracoes()
    {
        var c = await Criar(api);
        var soRaiz = new Api(c.Http, Tokens.Gerar(c.EmpresaId, [ModuleCodes.Raiz]));

        Assert.Equal(403, (await soRaiz.Get("/api/salao/perfis-acesso")).Codigo);
        Assert.Equal(403, (await c.Recepcao.Get("/api/salao/perfis-acesso")).Codigo);
    }

    [Fato]
    public async Task Listagem_de_agendamentos_tem_periodo_maximo()
    {
        var c = await Criar(api);
        var r = await c.Admin.Get("/api/salao/agendamentos?de=2000-01-01T00:00:00Z&ate=2100-01-01T00:00:00Z");
        Assert.Equal(400, r.Codigo);
    }
}
