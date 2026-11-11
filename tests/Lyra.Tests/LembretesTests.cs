using System.Globalization;
using System.Text.Json;
using Lyra.Infrastructure.Persistence;
using Lyra.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class LembretesTests(ApiFixture api)
{
    private sealed record Contexto(Cenario C, int Cliente, string Email);

    private async Task<Contexto> Preparar(object? lembretes = null, bool? resumo = null, bool emails = true)
    {
        var c = await Criar(api);
        await api.ExecutarNoBanco(
            "INSERT INTO core.empresas (id, razao_social, nome_fantasia, email) VALUES ($1, 'Salão Teste Ltda', 'Salão Bela', 'contato@salao.teste')",
            c.EmpresaId);
        var atual = Exigir(await c.Admin.Get("/api/salao/configuracao"), 200).Json;
        Exigir(await c.Admin.Put("/api/salao/configuracao", new
        {
            segmento = atual.Str("segmento"),
            fusoHorario = atual.Str("fusoHorario"),
            enviarEmails = emails,
            lembretesHoras = lembretes,
            resumoDiario = resumo,
        }), 200);
        var email = $"lia.{c.EmpresaId}@cliente.teste";
        var telefone = "119" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
        var cliente = Exigir(await c.Admin.Post("/api/salao/clientes", new { nome = "Lia Ramos", telefone, email, ativo = true }), 201).Id;
        return new Contexto(c, cliente, email);
    }

    /// <summary>Hora local da empresa (UTC−3) como instante UTC.</summary>
    private static DateTime Instante(DateOnly dia, string hora) =>
        DateTime.Parse(Utc(dia, hora), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private MensageriaFalsa.Pedido[] Lembretes(string email) =>
        api.Mensageria.Para(email).Where(p => p.Campo("assunto").StartsWith("Lembrete", StringComparison.Ordinal)).ToArray();

    private Task<int> LembretesNaFila(int agendamento) => NoBanco(db => db.Notificacoes
        .CountAsync(n => n.AgendamentoId == agendamento && n.Evento == "agendamento_lembrete" && n.Canal == "email"));

    [Fato]
    public async Task Lembrete_de_24h_sai_uma_vez_e_pede_confirmacao()
    {
        var x = await Preparar();
        var id = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        await api.ProcessarFila();

        await api.ProgramarAvisos(Instante(x.C.Segunda, "14:00"));
        Assert.Equal(0, await LembretesNaFila(id));

        var vespera = Instante(x.C.Segunda, "15:30");
        await api.ProgramarAvisos(vespera);
        await api.ProgramarAvisos(vespera.AddMinutes(1));
        await api.ProgramarAvisos(Instante(x.C.Terca, "14:00"));
        Assert.Equal(1, await LembretesNaFila(id));

        await api.ProcessarFila();
        var p = Assert.Single(Lembretes(x.Email));        Assert.Equal("Lembrete: amanhã às 15:00 – Salão Bela", p.Campo("assunto"));
        Assert.Equal("agendamento_lembrete", p.Email.GetProperty("tags").GetProperty("evento").GetString());
        Assert.Equal($"agendamento:{id}", p.Corpo.GetProperty("referencia").GetString());
        var texto = p.Campo("texto");
        Assert.Contains("Passando para lembrar: seu horário em Salão Bela é amanhã (terça-feira) às 15:00.", texto);
        Assert.Contains($"{ApiFixture.UrlPublica}/c/", texto);
        Assert.Contains("Seu horário é amanhã", p.Campo("html"));
        // O convite já foi no e-mail do agendamento.
        Assert.Equal(JsonValueKind.Null, p.Email.GetProperty("anexos").ValueKind);
        Assert.DoesNotContain("convite em anexo", p.Campo("html"));
    }

    [Fato]
    public async Task Com_24h_e_2h_cada_lembrete_sai_na_sua_hora_e_confirmado_nao_pede_confirmacao()
    {
        var x = await Preparar(lembretes: new[] { 2, 24 });
        var config = Exigir(await x.C.Admin.Get("/api/salao/configuracao"), 200).Json;
        Assert.Equal([24, 2], config.GetProperty("lembretesHoras").EnumerateArray().Select(h => h.GetInt32()));
        var id = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        Exigir(await x.C.Admin.Patch($"/api/salao/agendamentos/{id}/status", new { status = "confirmado" }), 200);
        await api.ProcessarFila();

        await api.ProgramarAvisos(Instante(x.C.Segunda, "15:10"));
        await api.ProgramarAvisos(Instante(x.C.Terca, "12:30"));
        await api.ProgramarAvisos(Instante(x.C.Terca, "13:10"));
        await api.ProgramarAvisos(Instante(x.C.Terca, "13:20"));
        Assert.Equal(2, await LembretesNaFila(id));

        await api.ProcessarFila();
        var lembretes = Lembretes(x.Email);
        Assert.Equal(2, lembretes.Length);
        Assert.Equal(["Lembrete: amanhã às 15:00 – Salão Bela", "Lembrete: hoje às 15:00 – Salão Bela"],
            lembretes.Select(p => p.Campo("assunto")));
        Assert.All(lembretes, p => Assert.DoesNotContain("/c/", p.Campo("texto")));
    }

    [Fato]
    public async Task Sem_lembrete_para_cancelado_marcado_em_cima_da_hora_ou_empresa_sem_emails()
    {
        var x = await Preparar();
        var cancelado = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        Exigir(await x.C.Admin.Patch($"/api/salao/agendamentos/{cancelado}/status", new { status = "cancelado" }), 200);
        var emCima = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "16:00");
        // Marcado 3 h antes: o e-mail do agendamento acabou de sair.
        await api.ExecutarNoBanco("UPDATE lyra.agendamentos SET data_criacao = $2 WHERE id = $1", emCima, Instante(x.C.Terca, "13:00"));
        var semEmails = await Preparar(emails: false);
        var outro = await semEmails.C.AgendarOk(semEmails.Cliente, semEmails.C.Ana, [semEmails.C.Consulta], semEmails.C.Terca, "15:00");

        await api.ProgramarAvisos(Instante(x.C.Terca, "13:30"));

        Assert.Equal(0, await LembretesNaFila(cancelado));
        Assert.Equal(0, await LembretesNaFila(emCima));
        Assert.Equal(0, await LembretesNaFila(outro));
        Exigir(await x.C.Admin.Put("/api/salao/configuracao", new
        {
            segmento = "beleza",
            fusoHorario = "America/Sao_Paulo",
            lembretesHoras = new[] { 0 },
        }), 400);
    }

    [Fato]
    public async Task Resumo_do_dia_sai_as_7h_para_cada_profissional_com_email()
    {
        var x = await Preparar(resumo: true, emails: false);
        var pessoaAna = await Pessoa(x.C, x.C.Ana);
        var pessoaBruno = await Pessoa(x.C, x.C.Bruno);
        var emailAna = $"ana.{x.C.EmpresaId}@salao.teste";
        var emailBruno = $"bruno.{x.C.EmpresaId}@salao.teste";
        await api.ExecutarNoBanco("INSERT INTO core.pessoas (id, nome_completo, email_login) VALUES ($1, 'Ana', $2)", pessoaAna, emailAna);
        // Sem e-mail de login: vale o do usuário ligado à pessoa.
        await api.ExecutarNoBanco("INSERT INTO core.pessoas (id, nome_completo) VALUES ($1, 'Bruno')", pessoaBruno);
        await api.ExecutarNoBanco("INSERT INTO core.usuarios (id, email) VALUES ($1, $2)", pessoaBruno, emailBruno);
        await api.ExecutarNoBanco("INSERT INTO core.usuario_pessoa (usuario_id, pessoa_id) VALUES ($1, $1)", pessoaBruno);

        var primeiro = await x.C.AgendarOk(x.Cliente, x.C.Ana, [x.C.Consulta], x.C.Terca, "15:00");
        await x.C.AgendarOk(x.C.Maria, x.C.Ana, [x.C.Retorno], x.C.Terca, "09:30");
        Exigir(await x.C.Admin.Patch($"/api/salao/agendamentos/{primeiro}/status", new { status = "confirmado" }), 200);
        await x.C.AgendarOk(x.C.Joao, x.C.Bruno, [x.C.Avaliacao], x.C.Terca, "10:00");
        await x.C.AgendarOk(x.C.Joao, x.C.Bruno, [x.C.Avaliacao], x.C.Quarta, "10:00");

        await api.ProgramarAvisos(Instante(x.C.Terca, "06:50"));
        await api.ProcessarFila();
        Assert.Empty(api.Mensageria.Para(emailAna));

        await api.ProgramarAvisos(Instante(x.C.Terca, "07:01"));
        await api.ProgramarAvisos(Instante(x.C.Terca, "07:02"));
        await api.ProcessarFila();

        var ana = Assert.Single(api.Mensageria.Para(emailAna));
        Assert.Equal("Sua agenda de hoje: 2 horários – Salão Bela", ana.Campo("assunto"));
        Assert.Equal("resumo_dia", ana.Email.GetProperty("tags").GetProperty("evento").GetString());
        var texto = ana.Campo("texto");
        Assert.StartsWith("Bom dia, Ana!", texto);
        Assert.Contains("você tem 2 horários, 1 já confirmado.", texto);
        Assert.True(texto.IndexOf("09:30", StringComparison.Ordinal) < texto.IndexOf("15:00", StringComparison.Ordinal));
        Assert.Contains("Maria · Retorno", texto);
        Assert.Contains("Lia Ramos · Consulta (confirmado)", texto);
        Assert.Contains($"{ApiFixture.UrlPublica}/agenda?data={Dia(x.C.Terca)}&visao=dia", texto);

        var bruno = Assert.Single(api.Mensageria.Para(emailBruno));
        Assert.Equal("Sua agenda de hoje: 1 horário – Salão Bela", bruno.Campo("assunto"));
        Assert.DoesNotContain("Quarta", bruno.Campo("texto"), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> Pessoa(Cenario c, int profissional) =>
        Exigir(await c.Admin.Get($"/api/salao/profissionais/{profissional}"), 200).Json.Int("pessoaId");

    private async Task<T> NoBanco<T>(Func<LyraDbContext, Task<T>> consulta)
    {
        using var scope = api.Servicos.CreateScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<LyraDbContext>());
    }
}
