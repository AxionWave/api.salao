using Lyra.Tests.Infra;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

/// <summary>R-17: a trava por profissional impede dois agendamentos no mesmo horário, mesmo com pedidos simultâneos.</summary>
[Collection(ColecaoApi.Nome)]
public sealed class ConcorrenciaTests(ApiFixture api)
{
    private static async Task<int[]> Simultaneos(IEnumerable<Func<Task<Resposta>>> pedidos)
    {
        using var largada = new SemaphoreSlim(0);
        var tarefas = pedidos.Select(async p => { await largada.WaitAsync(); return (await p()).Codigo; }).ToArray();
        largada.Release(tarefas.Length);
        return await Task.WhenAll(tarefas);
    }

    private static async Task<int[]> Clientes(Cenario c, int n)
    {
        var ids = new int[n];
        for (var i = 0; i < n; i++) ids[i] = await c.NovoCliente($"Cliente {i}");
        return ids;
    }

    [Fato]
    public async Task Dez_pedidos_no_mesmo_horario_gravam_um_so()
    {
        var c = await Criar(api);
        var clientes = await Clientes(c, 10);

        var codigos = await Simultaneos(clientes.Select(cli => (Func<Task<Resposta>>)(() =>
            c.Agendar(cli, c.Ana, [c.Consulta], c.Quarta, "09:00"))));

        Assert.Equal(1, codigos.Count(x => x == 201));
        Assert.Equal(9, codigos.Count(x => x == 409));
    }

    [Fato]
    public async Task Pedidos_em_horarios_diferentes_e_profissionais_diferentes_passam()
    {
        var c = await Criar(api);
        var clientes = await Clientes(c, 10);

        var codigos = await Simultaneos(clientes.Select((cli, i) => (Func<Task<Resposta>>)(() =>
            c.Agendar(cli, c.Bruno, [c.Retorno], c.Quarta, $"{9 + i % 5:00}:00"))));
        var paralelos = await Simultaneos([
            () => c.Agendar(c.Maria, c.Ana, [c.Retorno], c.Quinta, "16:00"),
            () => c.Agendar(c.Joao, c.Bruno, [c.Retorno], c.Quinta, "16:00"),
        ]);

        Assert.Equal(5, codigos.Count(x => x == 201));
        Assert.All(paralelos, x => Assert.Equal(201, x));
    }

    [Fato]
    public async Task Cinco_remarcacoes_para_o_mesmo_horario_gravam_uma_so()
    {
        var c = await Criar(api);
        var ids = new List<int>();
        foreach (var hora in new[] { "08:00", "09:00", "10:00", "11:00", "12:00" })
        {
            ids.Add(await c.AgendarOk(c.Maria, c.Bruno, [c.Retorno], c.Quinta, hora));
        }

        var codigos = await Simultaneos(ids.Select(id => (Func<Task<Resposta>>)(() =>
            c.Admin.Post($"/api/salao/agendamentos/{id}/remarcar", Corpo(c.Maria, c.Bruno, [c.Retorno], Utc(c.Quinta, "14:00"))))));

        Assert.Equal(1, codigos.Count(x => x == 201));
    }
}
