using System.Text.Json;
using System.Threading.Channels;
using Lyra.Core.Modules;
using Lyra.Tests.Infra;
using Microsoft.AspNetCore.SignalR.Client;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class PainelTests(ApiFixture api)
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(5);

    /// <summary>Guarda tudo o que o hub mandou, por evento.</summary>
    private sealed class Ouvinte : IAsyncDisposable
    {
        private readonly HubConnection conexao;
        public Channel<JsonElement> Agenda { get; } = Channel.CreateUnbounded<JsonElement>();
        public Channel<JsonElement> Avisos { get; } = Channel.CreateUnbounded<JsonElement>();

        public Ouvinte(HubConnection conexao)
        {
            this.conexao = conexao;
            conexao.On<JsonElement>("agendaAlterada", e => Agenda.Writer.TryWrite(e));
            conexao.On<JsonElement>("notificacao", e => Avisos.Writer.TryWrite(e));
        }

        public ValueTask DisposeAsync() => conexao.DisposeAsync();
    }

    private async Task<Ouvinte> Ouvir(int empresaId, int usuarioId = 1, string[]? modulos = null) =>
        new(await api.ConectarPainel(Tokens.Gerar(empresaId, modulos ?? Tokens.Todos, usuarioId: usuarioId)));

    private static async Task<JsonElement> Proximo(Channel<JsonElement> canal)
    {
        using var limite = new CancellationTokenSource(Espera);
        return await canal.Reader.ReadAsync(limite.Token);
    }

    private static async Task Nada(Channel<JsonElement> canal)
    {
        await Task.Delay(300);
        Assert.False(canal.Reader.TryRead(out var e), $"Não devia chegar nada, veio {e}");
    }

    [Fato]
    public async Task Agendamento_novo_avisa_a_agenda_e_o_sino_da_mesma_empresa()
    {
        var c = await Criar(api);
        var outra = await Criar(api);
        await using var recepcao = await Ouvir(c.EmpresaId, usuarioId: 2);
        await using var deFora = await Ouvir(outra.EmpresaId);

        var id = await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");

        var agenda = await Proximo(recepcao.Agenda);
        Assert.Equal(c.Ana, agenda.GetProperty("profissionalId").GetInt32());
        await api.ProcessarFila();
        var aviso = await Proximo(recepcao.Avisos);
        Assert.Equal("agendamento_criado", aviso.Str("evento"));
        Assert.Equal(id, aviso.GetProperty("agendamentoId").GetInt32());
        Assert.Equal("Maria", aviso.Str("cliente"));
        Assert.Equal("Ana", aviso.Str("profissional"));
        Assert.Equal($"{c.Terca:dd/MM/yyyy} 15:00", aviso.Str("inicioLocal"));
        Assert.Equal(1, aviso.GetProperty("autorId").GetInt32());
        await Nada(deFora.Agenda);
        await Nada(deFora.Avisos);
    }

    [Fato]
    public async Task Sino_lista_sem_os_avisos_do_proprio_usuario_e_zera_ao_ver()
    {
        var c = await Criar(api);
        var recepcao = new Api(c.Http, Tokens.Gerar(c.EmpresaId, Tokens.Recepcao, usuarioId: 2));
        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");
        await c.AgendarOk(c.Joao, c.Bruno, [c.Retorno], c.Terca, "10:00");
        await api.ProcessarFila();

        var doAdmin = Exigir(await c.Admin.Get("/api/salao/painel/notificacoes"), 200).Json;
        Assert.Empty(doAdmin.GetProperty("itens").EnumerateArray());

        var sino = Exigir(await recepcao.Get("/api/salao/painel/notificacoes"), 200).Json;
        Assert.Equal(2, sino.GetProperty("itens").GetArrayLength());
        Assert.Equal(2, sino.GetProperty("naoLidas").GetInt32());
        Assert.Equal("João", sino.GetProperty("itens")[0].Str("cliente"));

        Exigir(await recepcao.Post("/api/salao/painel/notificacoes/vistas", new { }), 204);
        Assert.Equal(0, Exigir(await recepcao.Get("/api/salao/painel/notificacoes"), 200).Json.GetProperty("naoLidas").GetInt32());

        // O sino é da equipe: não aparece nas mensagens enviadas ao cliente.
        var mensagens = Exigir(await c.Admin.Get($"/api/salao/clientes/{c.Maria}/notificacoes"), 200).Json;
        Assert.DoesNotContain(mensagens.EnumerateArray(), m => m.Str("canal") == "painel");
    }

    [Fato]
    public async Task Cliente_confirmando_pelo_link_avisa_a_equipe()
    {
        var c = await Criar(api);
        await api.ExecutarNoBanco(
            "INSERT INTO core.empresas (id, razao_social, nome_fantasia, email) VALUES ($1, 'Salão', 'Salão', 'contato@salao.teste')",
            c.EmpresaId);
        var cliente = Exigir(await c.Admin.Post("/api/salao/clientes", new
        {
            nome = "Carla",
            telefone = "119" + Random.Shared.Next(10_000_000, 99_999_999),
            email = $"carla.{c.EmpresaId}@cliente.teste",
            ativo = true,
        }), 201).Id;
        var atual = Exigir(await c.Admin.Get("/api/salao/configuracao"), 200).Json;
        Exigir(await c.Admin.Put("/api/salao/configuracao", new
        {
            segmento = atual.Str("segmento"),
            fusoHorario = atual.Str("fusoHorario"),
            enviarEmails = true,
        }), 200);
        await c.AgendarOk(cliente, c.Ana, [c.Consulta], c.Terca, "15:00");
        await api.ProcessarFila();
        var texto = api.Mensageria.Para($"carla.{c.EmpresaId}@cliente.teste").Single().Campo("texto");
        var token = System.Text.RegularExpressions.Regex.Match(texto, @"/c/([A-Za-z0-9_-]+)").Groups[1].Value;
        await using var equipe = await Ouvir(c.EmpresaId);

        var confirmar = await c.Http.PostAsync($"/api/salao/publico/agendamentos/{token}/confirmar", null);
        confirmar.EnsureSuccessStatusCode();
        await Proximo(equipe.Agenda);
        await api.ProcessarFila();

        var aviso = await Proximo(equipe.Avisos);
        Assert.Equal("agendamento_confirmado", aviso.Str("evento"));
        Assert.Equal("cliente", aviso.Str("origem"));
        Assert.Equal(JsonValueKind.Null, aviso.GetProperty("autorId").ValueKind);
    }

    [Fato]
    public async Task Hub_exige_token_valido_e_o_modulo_agenda()
    {
        var c = await Criar(api);

        await Assert.ThrowsAnyAsync<Exception>(() => api.ConectarPainel("token-invalido"));

        var semAgenda = await api.ConectarPainel(Tokens.Gerar(c.EmpresaId, [ModuleCodes.Raiz, ModuleCodes.Clientes]));
        var fechou = new TaskCompletionSource();
        semAgenda.Closed += _ => { fechou.TrySetResult(); return Task.CompletedTask; };
        if (semAgenda.State != HubConnectionState.Disconnected)
        {
            await fechou.Task.WaitAsync(Espera);
        }
        await semAgenda.DisposeAsync();
    }
}
