using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Lyra.Tests.Infra;

/// <summary>
/// Faz o papel da AW.Mensageria: guarda cada pedido de envio e responde com o status configurado por destinatário.
/// Repetir a mesma chave devolve o mesmo envio, como a de verdade.
/// </summary>
public sealed class MensageriaFalsa : HttpMessageHandler
{
    public sealed record Pedido(string Para, JsonElement Corpo, string? Token, string? Empresa, string? Sistema)
    {
        public string Chave => Corpo.GetProperty("chave").GetString()!;
        public JsonElement Email => Corpo.GetProperty("email");
        public string Campo(string nome) => Email.GetProperty(nome).GetString()!;
    }

    private readonly ConcurrentDictionary<string, string> idPorChave = new();

    public ConcurrentQueue<Pedido> Recebidos { get; } = new();

    public ConcurrentDictionary<string, HttpStatusCode> RespostaPorDestino { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Destinos que a Mensageria trata como descadastrados (aceita, mas não entrega).</summary>
    public ConcurrentDictionary<string, byte> Descadastrados { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Um por envio: pedidos repetidos com a mesma chave não contam.</summary>
    public Pedido[] Para(string email) => Recebidos
        .Where(p => p.Para.Equals(email, StringComparison.OrdinalIgnoreCase))
        .GroupBy(p => p.Chave)
        .Select(g => g.First())
        .ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var corpo = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
        var para = corpo.GetProperty("destino").GetString()!;
        var status = RespostaPorDestino.TryGetValue(para, out var s) ? s : HttpStatusCode.Accepted;
        if (status != HttpStatusCode.Accepted)
        {
            return Json(status, """{"error":"validacao","message":"Pedido recusado (simulado)."}""");
        }

        Recebidos.Enqueue(new Pedido(
            para,
            corpo,
            Cabecalho(request, "X-Internal-Service-Token"),
            Cabecalho(request, "X-Empresa-Id"),
            Cabecalho(request, "X-Origin-System")));
        var chave = corpo.GetProperty("chave").GetString()!;
        var novo = $"env_{Guid.NewGuid():N}";
        var id = idPorChave.GetOrAdd(chave, novo);
        var situacao = Descadastrados.ContainsKey(para) ? "descadastrado" : "pendente";
        return Json(id == novo ? HttpStatusCode.Accepted : HttpStatusCode.OK, $$"""{"id":"{{id}}","status":"{{situacao}}"}""");
    }

    private static string? Cabecalho(HttpRequestMessage request, string nome) =>
        request.Headers.TryGetValues(nome, out var valores) ? valores.First() : null;

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
