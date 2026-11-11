using System.Globalization;
using System.Text.Json;
using Lyra.Infrastructure.Persistence;
using Lyra.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class AvisosTests(ApiFixture api)
{
    private const string Rota = "/api/salao/configuracao/avisos";

    private sealed record Contexto(Cenario C, int Cliente, string Email);

    private async Task<Contexto> Preparar()
    {
        var c = await Criar(api);
        await api.ExecutarNoBanco(
            "INSERT INTO core.empresas (id, razao_social, nome_fantasia, email, telefone, endereco, cidade, estado) VALUES ($1, $2, $3, $4, $5, $6, $7, $8)",
            c.EmpresaId, "Salão Teste Ltda", "Salão Bela", "contato@salao.teste", "(11) 3333-4444", "Rua A, 10", "São Paulo", "SP");
        var email = $"carla.{c.EmpresaId}@cliente.teste";
        var telefone = "119" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
        var cliente = Exigir(await c.Admin.Post("/api/salao/clientes", new { nome = "Carla Souza", telefone, email, ativo = true }), 201).Id;
        return new Contexto(c, cliente, email);
    }

    private static object Evento(string evento, bool cliente = true, bool equipe = true, string? assunto = null, string? mensagem = null) =>
        new { evento, cliente, equipe, assunto, mensagem };

    private static Task<Resposta> Salvar(Cenario c, object[] eventos, string? recado = null, bool enviarEmails = true) =>
        c.Admin.Put(Rota, new { enviarEmails, lembretesHoras = new[] { 24 }, resumoDiario = false, eventos, recado });

    private Task<string[]> Canais(int agendamento) => NoBanco(db => db.Notificacoes
        .Where(n => n.AgendamentoId == agendamento)
        .Select(n => n.Canal)
        .ToArrayAsync());

    private static JsonElement EventoSalvo(Resposta r, string evento) =>
        r.Json.GetProperty("avisos").GetProperty("eventos").EnumerateArray().Single(e => e.Str("evento") == evento);

    [Fato]
    public async Task Comeca_com_tudo_ligado_e_os_textos_padrao()
    {
        var c = await Criar(api);

        var r = Exigir(await c.Admin.Get(Rota), 200).Json;

        var avisos = r.GetProperty("avisos");
        Assert.False(avisos.GetProperty("enviarEmails").GetBoolean());
        Assert.Equal([24], avisos.GetProperty("lembretesHoras").EnumerateArray().Select(h => h.GetInt32()));
        var eventos = avisos.GetProperty("eventos").EnumerateArray().ToArray();
        Assert.Equal(5, eventos.Length);
        Assert.All(eventos, e => Assert.True(e.GetProperty("cliente").GetBoolean()));
        Assert.All(eventos, e => Assert.True(e.Nulo("assunto") && e.Nulo("mensagem")));
        Assert.False(eventos.Single(e => e.Str("evento") == "agendamento_lembrete").GetProperty("equipe").GetBoolean());
        var criado = r.GetProperty("modelos").EnumerateArray().Single(m => m.Str("evento") == "agendamento_criado");
        Assert.Equal("Horário marcado: {data} às {hora} – {empresa}", criado.Str("assunto"));
        Assert.Contains(r.GetProperty("variaveis").EnumerateArray(), v => v.Str("nome") == "cliente");
    }

    [Fato]
    public async Task Cada_evento_liga_e_desliga_o_email_do_cliente_e_o_push_da_equipe()
    {
        var x = await Preparar();
        Exigir(await Salvar(x.C, [Evento("agendamento_criado", cliente: false, equipe: true)]), 200);
        var semEmail = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");

        Exigir(await Salvar(x.C, [Evento("agendamento_criado", cliente: true, equipe: false)]), 200);
        var semPush = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Quarta, "15:00");

        var canais1 = await Canais(semEmail);
        Assert.DoesNotContain("email", canais1);
        Assert.Contains("push", canais1);
        Assert.Contains("painel", canais1);
        var canais2 = await Canais(semPush);
        Assert.Contains("email", canais2);
        Assert.DoesNotContain("push", canais2);
        // O sino mostra sempre.
        Assert.Contains("painel", canais2);
    }

    [Fato]
    public async Task Email_sai_com_os_textos_da_empresa_e_o_link_de_descadastro()
    {
        var x = await Preparar();
        Exigir(await Salvar(x.C,
            [Evento("agendamento_criado",
                assunto: "Te esperamos {dia} às {hora}, {cliente}!",
                mensagem: "Oi {cliente}! Sua {servico} com {profissional} é {quando}.\nDúvidas: {telefone}.")],
            recado: "Chegue 10 minutos antes."), 200);

        await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        await api.ProcessarFila();

        var p = Assert.Single(api.Mensageria.Para(x.Email));
        Assert.Equal($"Te esperamos {x.C.Terca:dd/MM} às 15:00, Carla!", p.Campo("assunto"));
        var dia = x.C.Terca.ToDateTime(TimeOnly.MinValue).ToString("dddd, dd/MM", CultureInfo.GetCultureInfo("pt-BR"));
        var texto = p.Campo("texto");
        Assert.Contains($"Oi Carla! Sua Consulta com Ana é {dia} às 15:00.\nDúvidas: (11) 3333-4444.", texto.ReplaceLineEndings("\n"));
        Assert.Contains("Chegue 10 minutos antes.", texto);
        Assert.Contains("Não quer mais receber estes e-mails? {{descadastro}}", texto);
        var html = p.Campo("html");
        Assert.Contains($"Oi Carla! Sua Consulta com Ana é {dia} às 15:00.<br>Dúvidas: (11) 3333-4444.", html);
        Assert.Contains("Chegue 10 minutos antes.", html);
        Assert.Contains("""<a href="{{descadastro}}" """.TrimEnd(), html);
    }

    [Fato]
    public async Task Texto_vazio_ou_igual_ao_padrao_fica_padrao_e_variavel_errada_eh_recusada()
    {
        var c = await Criar(api);

        var r = Exigir(await Salvar(c,
            [
                Evento("agendamento_criado", assunto: "  Horário marcado: {data} às {hora} – {empresa} ", mensagem: "   "),
                Evento("agendamento_lembrete", equipe: true, mensagem: "Até {dia}!"),
            ], recado: ""), 200);
        var criado = EventoSalvo(r, "agendamento_criado");
        Assert.True(criado.Nulo("assunto") && criado.Nulo("mensagem"));
        var lembrete = EventoSalvo(r, "agendamento_lembrete");
        Assert.Equal("Até {dia}!", lembrete.Str("mensagem"));
        // O lembrete não tem push.
        Assert.False(lembrete.GetProperty("equipe").GetBoolean());
        Assert.True(r.Json.GetProperty("avisos").Nulo("recado"));

        var erro = Exigir(await Salvar(c, [Evento("agendamento_criado", mensagem: "Olá {nome}, {cliente}")]), 400);
        Assert.Contains("{nome}", erro.Mensagem);
        Exigir(await Salvar(c, [Evento("agendamento_inventado")]), 400);
        Exigir(await Salvar(c, [Evento("agendamento_criado", assunto: new string('a', 151))]), 400);
        Exigir(await c.Admin.Put(Rota, new { enviarEmails = true, lembretesHoras = new[] { 100 }, resumoDiario = false }), 400);
    }

    [Fato]
    public async Task Previa_mostra_o_email_com_os_textos_ainda_nao_salvos()
    {
        var x = await Preparar();

        var lembrete = Exigir(await x.C.Admin.Post($"{Rota}/previa", new
        {
            evento = "agendamento_lembrete",
            mensagem = "Oi {cliente}, até {quando}!",
            recado = "Traga o cartão do convênio.",
        }), 200).Json;
        Assert.StartsWith("Lembrete: amanhã às 15:00 – Salão Bela", lembrete.Str("assunto"));
        var html = lembrete.Str("html");
        Assert.Contains("Oi Maria, até amanhã (", html);
        Assert.Contains("Traga o cartão do convênio.", html);
        Assert.Contains("Consulta", html);
        Assert.DoesNotContain("{{descadastro}}", html);

        var cancelado = Exigir(await x.C.Admin.Post($"{Rota}/previa", new
        {
            evento = "agendamento_cancelado",
            recado = "Traga o cartão do convênio.",
        }), 200).Json;
        Assert.StartsWith("Horário cancelado:", cancelado.Str("assunto"));
        Assert.DoesNotContain("Traga o cartão", cancelado.Str("texto"));

        Exigir(await x.C.Admin.Post($"{Rota}/previa", new { evento = "agendamento_criado", assunto = "{xyz}" }), 400);
        Exigir(await x.C.Admin.Post($"{Rota}/previa", new { evento = "outro" }), 400);
    }

    [Fato]
    public async Task So_quem_configura_ve_e_muda_os_avisos()
    {
        var c = await Criar(api);

        Exigir(await c.Recepcao.Get(Rota), 403);
        Exigir(await c.Recepcao.Put(Rota, new { enviarEmails = true, lembretesHoras = new[] { 24 }, resumoDiario = false }), 403);
        Exigir(await c.Recepcao.Post($"{Rota}/previa", new { evento = "agendamento_criado" }), 403);
    }

    private async Task<T> NoBanco<T>(Func<LyraDbContext, Task<T>> consulta)
    {
        using var scope = api.Servicos.CreateScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<LyraDbContext>());
    }
}
