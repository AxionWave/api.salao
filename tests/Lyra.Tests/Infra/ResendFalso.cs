using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Lyra.Tests.Infra;

/// <summary>Faz o papel da API do Resend: guarda cada e-mail e responde com o status configurado por destinatário.</summary>
public sealed class ResendFalso : HttpMessageHandler
{
    public sealed record Pedido(string Para, JsonElement Corpo, string? ChaveIdempotencia, string? Autorizacao);

    public ConcurrentQueue<Pedido> Recebidos { get; } = new();

    public ConcurrentDictionary<string, HttpStatusCode> RespostaPorDestino { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Pedido[] Para(string email) => Recebidos.Where(p => p.Para.Equals(email, StringComparison.OrdinalIgnoreCase)).ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var corpo = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
        var para = corpo.GetProperty("to")[0].GetString()!;
        var status = RespostaPorDestino.TryGetValue(para, out var s) ? s : HttpStatusCode.OK;
        if (status == HttpStatusCode.OK)
        {
            Recebidos.Enqueue(new Pedido(
                para,
                corpo,
                request.Headers.TryGetValues("Idempotency-Key", out var chave) ? chave.First() : null,
                request.Headers.Authorization?.ToString()));
            return Json(status, $$"""{"id":"resend-{{Guid.NewGuid():N}}"}""");
        }
        return Json(status, """{"name":"validation_error","message":"Destino recusado (simulado)."}""");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
