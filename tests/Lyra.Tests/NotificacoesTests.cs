using System.Text.Json;
using Lyra.Infrastructure.Persistence;
using Lyra.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class NotificacoesTests(ApiFixture api)
{
    private static async Task<JsonElement[]> Historico(Cenario c, int cliente) =>
        Exigir(await c.Admin.Get($"/api/salao/clientes/{cliente}/notificacoes"), 200).Json.EnumerateArray().ToArray();

    private async Task<T> NoBanco<T>(Func<LyraDbContext, Task<T>> consulta)
    {
        using var scope = api.Servicos.CreateScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<LyraDbContext>());
    }

    private Task<JsonElement> Dados(long id) => NoBanco(async db =>
        JsonDocument.Parse((await db.Notificacoes.AsNoTracking().FirstAsync(n => n.Id == id)).Dados).RootElement.Clone());

    private static long IdDe(JsonElement n) => n.GetProperty("id").GetInt64();

    [Fato]
    public async Task Agendar_enfileira_a_mensagem_junto_com_o_agendamento()
    {
        var c = await Criar(api);
        var id = await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");

        var historico = await Historico(c, c.Maria);

        var n = Assert.Single(historico);
        Assert.Equal("agendamento_criado", n.Str("evento"));
        Assert.Equal("teste", n.Str("canal"));
        Assert.Equal("pendente", n.Str("status"));
        Assert.Equal(id, n.Int("agendamentoId"));
        Assert.Equal(0, n.Int("tentativas"));
        Assert.False(n.Nulo("destino"));
        var dados = await Dados(IdDe(n));
        Assert.Equal("Maria", dados.Str("cliente"));
        Assert.Equal("Ana", dados.Str("profissional"));
        Assert.Equal("Consulta", dados.GetProperty("servicos")[0].GetString());
        Assert.Equal($"{c.Terca:dd/MM/yyyy} 15:00", dados.Str("inicioLocal"));
    }

    [Fato]
    public async Task Agendamento_recusado_nao_enfileira_nada()
    {
        var c = await Criar(api);
        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");

        var conflito = await c.Agendar(c.Joao, c.Ana, [c.Consulta], c.Terca, "15:00");

        Assert.Equal(409, conflito.Codigo);
        Assert.Empty(await Historico(c, c.Joao));
    }

    [Fato]
    public async Task Cada_mudanca_relevante_gera_um_evento()
    {
        var c = await Criar(api);
        var id = await c.AgendarOk(c.Maria, c.Bruno, [c.Retorno], c.Terca, "08:00");

        Exigir(await c.Admin.Put($"/api/salao/agendamentos/{id}", Corpo(c.Maria, c.Bruno, [c.Retorno], Utc(c.Terca, "09:00"))), 200);
        Exigir(await c.Admin.Put($"/api/salao/agendamentos/{id}", Corpo(c.Maria, c.Bruno, [c.Retorno], Utc(c.Terca, "09:00"), "só observação")), 200);
        var novo = Exigir(await c.Admin.Post($"/api/salao/agendamentos/{id}/remarcar", Corpo(c.Maria, c.Bruno, [c.Retorno], Utc(c.Quarta, "10:00"))), 201).Id;
        Exigir(await c.Admin.Patch($"/api/salao/agendamentos/{novo}/status", new { status = "confirmado" }), 200);
        Exigir(await c.Admin.Patch($"/api/salao/agendamentos/{novo}/status", new { status = "confirmado" }), 200);
        Exigir(await c.Admin.Patch($"/api/salao/agendamentos/{novo}/status", new { status = "cancelado", motivo = "Viagem" }), 200);

        var historico = (await Historico(c, c.Maria)).OrderBy(IdDe).ToArray();

        Assert.Equal(
            new[] { "agendamento_criado", "agendamento_remarcado", "agendamento_remarcado", "agendamento_confirmado", "agendamento_cancelado" },
            historico.Select(n => n.Str("evento")));
        Assert.Equal(new[] { id, id, novo, novo, novo }, historico.Select(n => n.Int("agendamentoId")));
        var editado = await Dados(IdDe(historico[1]));
        Assert.Equal($"{c.Terca:dd/MM/yyyy} 08:00", editado.Str("inicioAnteriorLocal"));
        Assert.Equal($"{c.Terca:dd/MM/yyyy} 09:00", editado.Str("inicioLocal"));
        var remarcado = await Dados(IdDe(historico[2]));
        Assert.Equal($"{c.Terca:dd/MM/yyyy} 09:00", remarcado.Str("inicioAnteriorLocal"));
        Assert.Equal("Viagem", (await Dados(IdDe(historico[4]))).Str("motivo"));
    }

    [Fato]
    public async Task Processar_envia_e_registra_no_historico()
    {
        var c = await Criar(api);
        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");

        await api.ProcessarFila();

        var n = Assert.Single(await Historico(c, c.Maria));
        Assert.Equal("enviado", n.Str("status"));
        Assert.Equal(1, n.Int("tentativas"));
        Assert.False(n.Nulo("dataEnvio"));
        Assert.Contains(IdDe(n), api.Canal.Enviadas);
        await api.ProcessarFila();
        Assert.Equal(1, api.Canal.Enviadas.Count(e => e == IdDe(n)));
    }

    [Fato]
    public async Task Falha_tenta_de_novo_e_desiste_depois_da_ultima_espera()
    {
        var c = await Criar(api);
        var telefone = Exigir(await c.Admin.Get($"/api/salao/clientes/{c.Maria}"), 200).Json.Str("telefone");
        api.Canal.DestinosComFalha[telefone] = 0;
        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");
        var id = IdDe(Assert.Single(await Historico(c, c.Maria)));

        await api.ProcessarFila();
        var primeira = Assert.Single(await Historico(c, c.Maria));
        var proxima = await NoBanco(db => db.Notificacoes.Where(n => n.Id == id).Select(n => n.ProximaTentativa).FirstAsync());

        Assert.Equal("pendente", primeira.Str("status"));
        Assert.Equal(1, primeira.Int("tentativas"));
        Assert.Contains("simulado", primeira.Str("ultimoErro"));
        Assert.True(proxima > DateTime.UtcNow.AddSeconds(30), "A nova tentativa deve esperar pelo menos a primeira espera.");

        for (var tentativa = 2; tentativa <= 6; tentativa++)
        {
            await NoBanco(db => db.Notificacoes.Where(n => n.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ProximaTentativa, DateTime.UtcNow.AddSeconds(-1))));
            await api.ProcessarFila();
            var n = Assert.Single(await Historico(c, c.Maria));
            Assert.Equal(tentativa, n.Int("tentativas"));
            Assert.Equal(tentativa < 6 ? "pendente" : "falhou", n.Str("status"));
        }
        api.Canal.DestinosComFalha.TryRemove(telefone, out _);
    }

    [Fato]
    public async Task Historico_respeita_empresa_e_modulo()
    {
        var c = await Criar(api);
        var outra = await Criar(api);
        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");

        Assert.Equal(404, (await outra.Admin.Get($"/api/salao/clientes/{c.Maria}/notificacoes")).Codigo);
        Assert.Equal(403, (await c.Recepcao.Get($"/api/salao/clientes/{c.Maria}/notificacoes")).Codigo);
        Assert.Equal(404, (await c.Admin.Get("/api/salao/clientes/999999999/notificacoes")).Codigo);
    }
}
