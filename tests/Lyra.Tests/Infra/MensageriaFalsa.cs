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

    /// <summary>Pedidos de sair da lista de uma empresa (<c>{empresa}|{email}</c>), como o link do e-mail faz.</summary>
    public ConcurrentDictionary<string, byte> Saidas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool MensageriaFora { get; set; }

    private static string Saida(string? empresa, string email) => $"{empresa}|{email.Trim().ToLowerInvariant()}";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/api/mensageria/preferencias", StringComparison.Ordinal))
        {
            return await Preferencias(request, ct);
        }
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
        var transacional = corpo.TryGetProperty("transacional", out var t) && t.ValueKind == JsonValueKind.True;
        var situacao = Descadastrados.ContainsKey(para)
            || (!transacional && Saidas.ContainsKey(Saida(Cabecalho(request, "X-Empresa-Id"), para)))
            ? "descadastrado" : "pendente";
        return Json(id == novo ? HttpStatusCode.Accepted : HttpStatusCode.OK, $$"""{"id":"{{id}}","status":"{{situacao}}"}""");
    }

    private async Task<HttpResponseMessage> Preferencias(HttpRequestMessage request, CancellationToken ct)
    {
        if (MensageriaFora) return Json(HttpStatusCode.ServiceUnavailable, """{"error":"fora","message":"Fora do ar (simulado)."}""");
        var empresa = Cabecalho(request, "X-Empresa-Id");
        string destino;
        if (request.Method == HttpMethod.Put)
        {
            var corpo = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            destino = corpo.GetProperty("destino").GetString()!;
            if (corpo.GetProperty("receber").GetBoolean()) Saidas.TryRemove(Saida(empresa, destino), out _);
            else Saidas.TryAdd(Saida(empresa, destino), 0);
        }
        else
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            destino = query["destino"]!;
        }
        var pediu = Saidas.ContainsKey(Saida(empresa, destino));
        var bloqueio = Descadastrados.ContainsKey(destino) ? "\"devolvido\"" : "null";
        var recebe = !pediu && bloqueio == "null";
        return Json(HttpStatusCode.OK,
            $$"""{"recebe":{{(recebe ? "true" : "false")}},"pediuParaSair":{{(pediu ? "true" : "false")}},"bloqueio":{{bloqueio}}}""");
    }

    private static string? Cabecalho(HttpRequestMessage request, string nome) =>
        request.Headers.TryGetValues(nome, out var valores) ? valores.First() : null;

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
