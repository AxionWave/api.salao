using System.Globalization;
using System.Net;
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
    private async Task<Contexto> Preparar(bool ligar = true)
    {
        var c = await Criar(api);
        await api.ExecutarNoBanco(
            "INSERT INTO core.empresas (id, razao_social, nome_fantasia, email, telefone, endereco, cidade, estado) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)",
            c.EmpresaId, "Salão Teste Ltda", "Salão Bela <Teste>", "contato@salao.teste", "(11) 3333-4444", "Rua A, 10", "São Paulo", "SP");
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

    private static string Ics(ResendFalso.Pedido p) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(p.Corpo.GetProperty("attachments")[0].GetProperty("content").GetString()!));

    private static string Token(ResendFalso.Pedido p) =>
        LinkConfirmar().Match(p.Corpo.GetProperty("text").GetString()!).Groups[1].Value;

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
        var p = Assert.Single(api.Resend.Para(x.Email));
        Assert.Equal("enviado", n.Str("status"));
        Assert.Equal("\"Salão Bela Teste\" <naoresponda@lyra.lumensoft.com.br>", p.Corpo.GetProperty("from").GetString());
        Assert.Equal("contato@salao.teste", p.Corpo.GetProperty("reply_to").GetString());
        Assert.Equal($"Horário marcado: {x.C.Terca:dd/MM} às 15:00 – Salão Bela <Teste>", p.Corpo.GetProperty("subject").GetString());
        Assert.Equal($"lyra-notificacao-{n.GetProperty("id").GetInt64()}", p.ChaveIdempotencia);
        Assert.Equal("Bearer re_teste", p.Autorizacao);
        var texto = p.Corpo.GetProperty("text").GetString()!;
        Assert.Contains("Olá, Carla!", texto);
        Assert.Contains("Consulta", texto);
        Assert.Contains("Rua A, 10, São Paulo - SP", texto);
        Assert.Contains($"{ApiFixture.UrlPublica}/c/", texto);
        Assert.Contains("Salão Bela &lt;Teste&gt;", p.Corpo.GetProperty("html").GetString());
        var ics = Ics(p);
        Assert.Contains($"UID:lyra-agendamento-{id}@lyra.lumensoft.com.br", ics);
        Assert.Contains($"DTSTART:{x.C.Terca:yyyyMMdd}T180000Z", ics);
        Assert.Contains("METHOD:PUBLISH", ics);
        var provedor = await NoBanco(db => db.Notificacoes.Where(m => m.Id == n.GetProperty("id").GetInt64()).Select(m => m.ProvedorId).FirstAsync());
        Assert.StartsWith("resend-", provedor);
    }

    [Fato]
    public async Task Remarcar_e_cancelar_mantem_o_mesmo_evento_no_calendario()
    {
        var x = await Preparar();
        var id = await x.C.AgendarOk(x.Cliente, x.C.Bruno, [x.C.Retorno], x.C.Terca, "08:00");
        var novo = Exigir(await x.C.Admin.Post($"/api/salao/agendamentos/{id}/remarcar", Corpo(x.Cliente, x.C.Bruno, [x.C.Retorno], Utc(x.C.Quarta, "10:00"))), 201).Id;
        Exigir(await x.C.Admin.Patch($"/api/salao/agendamentos/{novo}/status", new { status = "cancelado", motivo = "Imprevisto" }), 200);

        await api.ProcessarFila();

        var pedidos = api.Resend.Para(x.Email);
        Assert.Equal(3, pedidos.Length);
        var uids = pedidos.Select(p => Uid().Match(Ics(p)).Groups[1].Value).Distinct().ToArray();
        Assert.Equal($"lyra-agendamento-{id}@lyra.lumensoft.com.br", Assert.Single(uids));
        var remarcado = pedidos.Single(p => p.Corpo.GetProperty("subject").GetString()!.StartsWith("Horário alterado"));
        Assert.Contains("passou de", remarcado.Corpo.GetProperty("text").GetString());
        var cancelado = pedidos.Single(p => p.Corpo.GetProperty("subject").GetString()!.StartsWith("Horário cancelado"));
        Assert.Contains("Motivo: Imprevisto", cancelado.Corpo.GetProperty("text").GetString());
        Assert.Contains("METHOD:CANCEL", Ics(cancelado));
        Assert.DoesNotContain("/c/", cancelado.Corpo.GetProperty("text").GetString());
    }

    [Fato]
    public async Task Recusa_do_provedor_falha_sem_novas_tentativas_e_erro_temporario_tenta_de_novo()
    {
        var recusado = await Preparar();
        var instavel = await Preparar();
        api.Resend.RespostaPorDestino[recusado.Email] = HttpStatusCode.UnprocessableEntity;
        api.Resend.RespostaPorDestino[instavel.Email] = HttpStatusCode.ServiceUnavailable;
        await recusado.C.AgendarOk(recusado.Cliente, recusado.C.Ana, [recusado.C.Consulta], recusado.C.Terca, "15:00");
        await instavel.C.AgendarOk(instavel.Cliente, instavel.C.Ana, [instavel.C.Consulta], instavel.C.Terca, "15:00");

        await api.ProcessarFila();

        var r = Assert.Single(await Emails(recusado.C, recusado.Cliente));
        var i = Assert.Single(await Emails(instavel.C, instavel.Cliente));
        Assert.Equal("falhou", r.Str("status"));
        Assert.Contains("Resend 422", r.Str("ultimoErro"));
        Assert.Equal("pendente", i.Str("status"));
        Assert.Contains("Resend 503", i.Str("ultimoErro"));
        api.Resend.RespostaPorDestino.Clear();
    }

    [Fato]
    public async Task Link_do_email_mostra_e_confirma_sem_login()
    {
        var x = await Preparar();
        var id = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        await api.ProcessarFila();
        var token = Token(Assert.Single(api.Resend.Para(x.Email)));
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
        Assert.Single(api.Resend.Para(x.Email));
    }

    [Fato]
    public async Task Link_invalido_ou_cancelado_nao_confirma()
    {
        var x = await Preparar();
        var id = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        await api.ProcessarFila();
        var token = Token(Assert.Single(api.Resend.Para(x.Email)));
        var adulterado = (token[0] == 'A' ? 'B' : 'A') + token[1..];
        Exigir(await x.C.Admin.Patch($"/api/salao/agendamentos/{id}/status", new { status = "cancelado" }), 200);
        var anonimo = x.C.Http;

        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync("/api/salao/publico/agendamentos/xyz")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync($"/api/salao/publico/agendamentos/{adulterado}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await anonimo.PostAsync($"/api/salao/publico/agendamentos/{token}/confirmar", null)).StatusCode);
        Assert.Equal("cancelado", Exigir(await x.C.Admin.Get($"/api/salao/agendamentos/{id}"), 200).Json.Str("status"));
    }

    private async Task<T> NoBanco<T>(Func<LyraDbContext, Task<T>> consulta)
    {
        using var scope = api.Servicos.CreateScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<LyraDbContext>());
    }
}
