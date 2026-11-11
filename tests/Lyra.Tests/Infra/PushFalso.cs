using System.Collections.Concurrent;
using System.Net;

namespace Lyra.Tests.Infra;

/// <summary>Faz o papel do serviço de push dos navegadores (FCM, Mozilla, Apple): guarda o que chegou.</summary>
public sealed class PushFalso : HttpMessageHandler
{
    public sealed record Pedido(string Endpoint, byte[] Corpo, IReadOnlyDictionary<string, string> Cabecalhos);

    public ConcurrentQueue<Pedido> Recebidos { get; } = new();

    /// <summary>Resposta por endpoint (padrão 201, como o FCM).</summary>
    public ConcurrentDictionary<string, HttpStatusCode> RespostaPorEndpoint { get; } = new();

    public Pedido[] Para(string endpoint) => Recebidos.Where(p => p.Endpoint == endpoint).ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var endpoint = request.RequestUri!.ToString();
        var corpo = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        var cabecalhos = request.Headers.Concat(request.Content.Headers)
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Recebidos.Enqueue(new Pedido(endpoint, corpo, cabecalhos));
        return new HttpResponseMessage(RespostaPorEndpoint.TryGetValue(endpoint, out var s) ? s : HttpStatusCode.Created);
    }
}
