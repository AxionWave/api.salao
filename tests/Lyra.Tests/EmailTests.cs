using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lyra.Infrastructure.Persistence;
using Lyra.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed partial class EmailTests(ApiFixture api)
{
    private sealed record Contexto(Cenario C, int Cliente, string Email);

    /// <summary>Empresa com cadastro no Core, envio de e-mail ligado e uma cliente com e-mail.</summary>
    private async Task<Contexto> Preparar(bool ligar = true, string? logoToken = null)
    {
        var c = await Criar(api);
        await api.ExecutarNoBanco(
            "INSERT INTO core.empresas (id, razao_social, nome_fantasia, email, telefone, endereco, cidade, estado, logo_token) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)",
            c.EmpresaId, "Salão Teste Ltda", "Salão Bela <Teste>", "contato@salao.teste", "(11) 3333-4444", "Rua A, 10", "São Paulo", "SP",
            (object?)logoToken ?? DBNull.Value);
        if (ligar) await LigarEmails(c, true);
        var email = $"carla.{c.EmpresaId}@cliente.teste";
        var telefone = "119" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
        var cliente = Exigir(await c.Admin.Post("/api/salao/clientes", new { nome = "Carla Souza", telefone, email, ativo = true }), 201).Id;
        return new Contexto(c, cliente, email);
    }

    private static async Task LigarEmails(Cenario c, bool ligar)
    {
        var atual = Exigir(await c.Admin.Get("/api/salao/configuracao"), 200).Json;
        Exigir(await c.Admin.Put("/api/salao/configuracao", new
        {
            segmento = atual.Str("segmento"),
            fusoHorario = atual.Str("fusoHorario"),
            enviarEmails = ligar,
        }), 200);
    }

    private static async Task<JsonElement[]> Emails(Cenario c, int cliente) =>
        Exigir(await c.Admin.Get($"/api/salao/clientes/{cliente}/notificacoes"), 200).Json
            .EnumerateArray().Where(n => n.Str("canal") == "email").OrderBy(n => n.GetProperty("id").GetInt64()).ToArray();

    private static string Ics(MensageriaFalsa.Pedido p) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(p.Email.GetProperty("anexos")[0].GetProperty("conteudoBase64").GetString()!));

    private static string Token(MensageriaFalsa.Pedido p) =>
        LinkConfirmar().Match(p.Campo("texto")).Groups[1].Value;

    [GeneratedRegex(@"/c/([A-Za-z0-9_-]+)")]
    private static partial Regex LinkConfirmar();

    [GeneratedRegex(@"UID:(\S+)")]
    private static partial Regex Uid();

    [Fato]
    public async Task Configuracao_comeca_desligada_e_nao_gera_email()
    {
        var x = await Preparar(ligar: false);

        await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");

        Assert.False(Exigir(await x.C.Admin.Get("/api/salao/configuracao"), 200).Json.GetProperty("enviarEmails").GetBoolean());
        Assert.Empty(await Emails(x.C, x.Cliente));
    }

    [Fato]
    public async Task Agendar_envia_email_com_dados_da_empresa_e_convite()
    {
        var x = await Preparar();
        var id = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");

        Assert.Equal("pendente", Assert.Single(await Emails(x.C, x.Cliente)).Str("status"));
        await api.ProcessarFila();

        var n = Assert.Single(await Emails(x.C, x.Cliente));
        var p = Assert.Single(api.Mensageria.Para(x.Email));
        Assert.Equal("enviado", n.Str("status"));
        Assert.Equal("email", p.Corpo.GetProperty("canal").GetString());
        Assert.Equal($"agendamento:{id}", p.Corpo.GetProperty("referencia").GetString());
        Assert.Equal("Salão Bela <Teste>", p.Campo("remetenteNome"));
        Assert.Equal("contato@salao.teste", p.Campo("responderPara"));
        Assert.Equal($"Horário marcado: {x.C.Terca:dd/MM} às 15:00 – Salão Bela <Teste>", p.Campo("assunto"));
        Assert.Equal("agendamento_criado", p.Email.GetProperty("tags").GetProperty("evento").GetString());
        Assert.Equal($"lyra-notificacao-{n.GetProperty("id").GetInt64()}", p.Chave);
        Assert.Equal(ApiFixture.TokenInterno, p.Token);
        Assert.Equal(x.C.EmpresaId.ToString(CultureInfo.InvariantCulture), p.Empresa);
        Assert.Equal("LYR", p.Sistema);
        var texto = p.Campo("texto");
        Assert.Contains("Olá, Carla!", texto);
        Assert.Contains("Consulta", texto);
        Assert.Contains("Rua A, 10, São Paulo - SP", texto);
        Assert.Contains($"{ApiFixture.UrlPublica}/c/", texto);
        Assert.Contains("- Consulta (", texto);
        Assert.Contains("150,00", texto);
        var html = p.Campo("html");
        Assert.Contains("Salão Bela &lt;Teste&gt;", html);
        Assert.Contains("https://calendar.google.com/calendar/render?action=TEMPLATE", html);
        Assert.Contains("https://www.google.com/maps/search/?api=1&amp;query=", html);
        // Telefone fixo: sem botão de WhatsApp.
        Assert.DoesNotContain("wa.me", html);
        var ics = Ics(p);
        Assert.Contains($"UID:lyra-agendamento-{id}@lyra.lumensoft.com.br", ics);
        Assert.Contains($"DTSTART:{x.C.Terca:yyyyMMdd}T180000Z", ics);
        Assert.Contains("METHOD:PUBLISH", ics);
        var provedor = await NoBanco(db => db.Notificacoes.Where(m => m.Id == n.GetProperty("id").GetInt64()).Select(m => m.ProvedorId).FirstAsync());
        Assert.StartsWith("env_", provedor);
    }

    [Fato]
    public async Task Email_mostra_o_logo_da_empresa_quando_ha_um()
    {
        const string token = "arq_AbCdEfGhIjKlMnOpQrStUv12";
        var comLogo = await Preparar(logoToken: token);
        var semLogo = await Preparar();
        var tokenInvalido = await Preparar(logoToken: "../../etc/passwd");
        foreach (var x in new[] { comLogo, semLogo, tokenInvalido })
        {
            await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        }

        await api.ProcessarFila();

        Assert.Contains($"<img src=\"{ApiFixture.UrlArquivos}/api/arquivos/publico/{token}\"", Assert.Single(api.Mensageria.Para(comLogo.Email)).Campo("html"));
        foreach (var x in new[] { semLogo, tokenInvalido })
        {
            var html = Assert.Single(api.Mensageria.Para(x.Email)).Campo("html");
            Assert.DoesNotContain("<img", html);
            Assert.Contains(">SB</td>", html);
        }
    }

    [Fato]
    public async Task Remarcar_e_cancelar_mantem_o_mesmo_evento_no_calendario()
    {
        var x = await Preparar();
        var id = await x.C.AgendarOk(x.Cliente, x.C.Bruno, [x.C.Retorno], x.C.Terca, "08:00");
        var novo = Exigir(await x.C.Admin.Post($"/api/salao/agendamentos/{id}/remarcar", Corpo(x.Cliente, x.C.Bruno, [x.C.Retorno], Utc(x.C.Quarta, "10:00"))), 201).Id;
        Exigir(await x.C.Admin.Patch($"/api/salao/agendamentos/{novo}/status", new { status = "cancelado", motivo = "Imprevisto" }), 200);

        await api.ProcessarFila();

        var pedidos = api.Mensageria.Para(x.Email);
        Assert.Equal(3, pedidos.Length);
        var uids = pedidos.Select(p => Uid().Match(Ics(p)).Groups[1].Value).Distinct().ToArray();
        Assert.Equal($"lyra-agendamento-{id}@lyra.lumensoft.com.br", Assert.Single(uids));
        var remarcado = pedidos.Single(p => p.Campo("assunto").StartsWith("Horário alterado"));
        Assert.Contains("passou de", remarcado.Campo("texto"));
        var cancelado = pedidos.Single(p => p.Campo("assunto").StartsWith("Horário cancelado"));
        Assert.Contains("Motivo: Imprevisto", cancelado.Campo("texto"));
        Assert.Contains("METHOD:CANCEL", Ics(cancelado));
        Assert.DoesNotContain("/c/", cancelado.Campo("texto"));
        Assert.DoesNotContain("calendar.google.com", cancelado.Campo("html"));
        Assert.Contains("Antes: ", remarcado.Campo("texto"));
    }

    [Fato]
    public async Task Pedido_recusado_falha_sem_novas_tentativas_e_erro_temporario_tenta_de_novo()
    {
        var recusado = await Preparar();
        var instavel = await Preparar();
        api.Mensageria.RespostaPorDestino[recusado.Email] = HttpStatusCode.BadRequest;
        api.Mensageria.RespostaPorDestino[instavel.Email] = HttpStatusCode.ServiceUnavailable;
        await recusado.C.AgendarOk(recusado.Cliente, recusado.C.Ana, [recusado.C.Consulta], recusado.C.Terca, "15:00");
        await instavel.C.AgendarOk(instavel.Cliente, instavel.C.Ana, [instavel.C.Consulta], instavel.C.Terca, "15:00");

        await api.ProcessarFila();

        var r = Assert.Single(await Emails(recusado.C, recusado.Cliente));
        var i = Assert.Single(await Emails(instavel.C, instavel.Cliente));
        Assert.Equal("falhou", r.Str("status"));
        Assert.Contains("Mensageria 400", r.Str("ultimoErro"));
        Assert.Equal("pendente", i.Str("status"));
        Assert.Contains("Mensageria 503", i.Str("ultimoErro"));
        api.Mensageria.RespostaPorDestino.Clear();
    }

    [Fato]
    public async Task Cliente_descadastrado_fica_como_falha_com_o_motivo()
    {
        var x = await Preparar();
        api.Mensageria.Descadastrados[x.Email] = 0;
        await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");

        await api.ProcessarFila();

        var n = Assert.Single(await Emails(x.C, x.Cliente));
        Assert.Equal("falhou", n.Str("status"));
        Assert.Contains("não receber e-mails", n.Str("ultimoErro"));
    }

    [Fato]
    public async Task Email_da_empresa_invalido_nao_vai_como_resposta()
    {
        var x = await Preparar();
        await api.ExecutarNoBanco("UPDATE core.empresas SET email = 'contato arroba salao' WHERE id = $1", x.C.EmpresaId);
        await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");

        await api.ProcessarFila();

        var p = Assert.Single(api.Mensageria.Para(x.Email));
        Assert.Equal(JsonValueKind.Null, p.Email.GetProperty("responderPara").ValueKind);
    }

    [Fato]
    public async Task Link_do_email_mostra_e_confirma_sem_login()
    {
        var x = await Preparar();
        var id = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        await api.ProcessarFila();
        var token = Token(Assert.Single(api.Mensageria.Para(x.Email)));
        var anonimo = x.C.Http;

        var ver = await anonimo.GetAsync($"/api/salao/publico/agendamentos/{token}");
        var dados = JsonDocument.Parse(await ver.Content.ReadAsStringAsync()).RootElement;
        var confirmar = await anonimo.PostAsync($"/api/salao/publico/agendamentos/{token}/confirmar", null);
        var deNovo = await anonimo.PostAsync($"/api/salao/publico/agendamentos/{token}/confirmar", null);
        await api.ProcessarFila();

        Assert.Equal(HttpStatusCode.OK, ver.StatusCode);
        Assert.Equal("Carla", dados.Str("cliente"));
        Assert.Equal("Salão Bela <Teste>", dados.Str("empresa"));
        Assert.Equal("agendado", dados.Str("status"));
        Assert.True(dados.GetProperty("podeConfirmar").GetBoolean());
        Assert.False(dados.TryGetProperty("email", out _));
        Assert.Equal(HttpStatusCode.OK, confirmar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deNovo.StatusCode);
        Assert.Equal("confirmado", Exigir(await x.C.Admin.Get($"/api/salao/agendamentos/{id}"), 200).Json.Str("status"));
        // Quem confirmou pelo link não recebe outro e-mail dizendo que confirmou.
        Assert.Single(api.Mensageria.Para(x.Email));
    }

    [Fato]
    public async Task Link_invalido_ou_cancelado_nao_confirma()
    {
        var x = await Preparar();
        var id = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        await api.ProcessarFila();
        var token = Token(Assert.Single(api.Mensageria.Para(x.Email)));
        var adulterado = (token[0] == 'A' ? 'B' : 'A') + token[1..];
        Exigir(await x.C.Admin.Patch($"/api/salao/agendamentos/{id}/status", new { status = "cancelado" }), 200);
        var anonimo = x.C.Http;

        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync("/api/salao/publico/agendamentos/xyz")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync($"/api/salao/publico/agendamentos/{adulterado}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await anonimo.PostAsync($"/api/salao/publico/agendamentos/{token}/confirmar", null)).StatusCode);
        Assert.Equal("cancelado", Exigir(await x.C.Admin.Get($"/api/salao/agendamentos/{id}"), 200).Json.Str("status"));
    }

    /// <summary>Agenda, envia e devolve a notificação com o id do envio que a Mensageria deu.</summary>
    private async Task<(Contexto X, long Notificacao, string Envio)> Enviado()
    {
        var x = await Preparar();
        await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        await api.ProcessarFila();
        var id = Assert.Single(await Emails(x.C, x.Cliente)).GetProperty("id").GetInt64();
        var envio = await NoBanco(db => db.Notificacoes.Where(m => m.Id == id).Select(m => m.ProvedorId!).FirstAsync());
        return (x, id, envio);
    }

    /// <summary>A Mensageria avisando, como ela chama na rede interna.</summary>
    private static async Task<HttpStatusCode> Avisar(Cenario c, string envio, long notificacao, string evento,
        string? erro = null, string? token = ApiFixture.TokenInterno, int? empresa = null)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/salao/internos/mensageria/envios")
        {
            Content = JsonContent.Create(new
            {
                id = envio,
                chave = $"lyra-notificacao-{notificacao}",
                referencia = "agendamento:1",
                canal = "email",
                evento,
                status = evento == "devolvido" ? "falhou" : evento,
                erro,
                data = DateTime.UtcNow,
            }),
        };
        if (token is not null) pedido.Headers.Add("X-Internal-Service-Token", token);
        pedido.Headers.Add("X-Empresa-Id", (empresa ?? c.EmpresaId).ToString(CultureInfo.InvariantCulture));
        pedido.Headers.Add("X-Origin-System", "MSG");
        using var resposta = await c.Http.SendAsync(pedido);
        return resposta.StatusCode;
    }

    [Fato]
    public async Task Aviso_da_mensageria_marca_entregue_e_spam_avisa_na_ficha()
    {
        var (x, n, envio) = await Enviado();

        Assert.Equal(HttpStatusCode.NoContent, await Avisar(x.C, envio, n, "entregue"));
        Assert.Equal("entregue", Assert.Single(await Emails(x.C, x.Cliente)).Str("status"));

        Assert.Equal(HttpStatusCode.NoContent, await Avisar(x.C, envio, n, "reclamacao"));
        var depois = Assert.Single(await Emails(x.C, x.Cliente));
        Assert.Equal("entregue", depois.Str("status"));
        Assert.Contains("spam", depois.Str("ultimoErro"));
    }

    [Fato]
    public async Task Devolucao_vira_falha_e_entrega_atrasada_nao_desfaz()
    {
        var (x, n, envio) = await Enviado();

        Assert.Equal(HttpStatusCode.NoContent, await Avisar(x.C, envio, n, "devolvido", "Devolvido pelo servidor do destino: caixa não existe"));
        Assert.Equal(HttpStatusCode.NoContent, await Avisar(x.C, envio, n, "entregue"));

        var e = Assert.Single(await Emails(x.C, x.Cliente));
        Assert.Equal("falhou", e.Str("status"));
        Assert.Contains("caixa não existe", e.Str("ultimoErro"));
    }

    [Fato]
    public async Task Aviso_so_vale_com_token_interno_e_para_a_propria_notificacao()
    {
        var (x, n, envio) = await Enviado();
        var outra = await Preparar();

        Assert.Equal(HttpStatusCode.Unauthorized, await Avisar(x.C, envio, n, "entregue", token: null));
        Assert.Equal(HttpStatusCode.Unauthorized, await Avisar(x.C, envio, n, "entregue", token: "errado"));
        Assert.Equal(HttpStatusCode.NotFound, await Avisar(x.C, envio, n, "entregue", empresa: outra.C.EmpresaId));
        Assert.Equal(HttpStatusCode.NotFound, await Avisar(x.C, "env_outro", n, "entregue"));
        Assert.Equal("enviado", Assert.Single(await Emails(x.C, x.Cliente)).Str("status"));
    }

    private async Task<T> NoBanco<T>(Func<LyraDbContext, Task<T>> consulta)
    {
        using var scope = api.Servicos.CreateScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<LyraDbContext>());
    }
}
