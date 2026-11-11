using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Notificacoes.Push;

/// <summary>Entrega uma mensagem cifrada ao serviço de push do navegador do aparelho.</summary>
public sealed class ClienteWebPush(HttpClient http, IOptions<WebPushOptions> opcoes)
{
    /// <summary>Aviso de agenda velho não ajuda: se o aparelho ficar um dia sem rede, o serviço descarta.</summary>
    private const int ValidadeSegundos = 24 * 60 * 60;

    /// <param name="topico">Mensagens com o mesmo tópico ainda não entregues são substituídas pela mais nova.</param>
    public async Task<HttpStatusCode> EnviarAsync(
        string endpoint, string p256dh, string auth, byte[] mensagem, string? topico, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(CifraWebPush.Cifrar(mensagem, p256dh, auth)),
        };
        pedido.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        pedido.Content.Headers.ContentEncoding.Add("aes128gcm");
        pedido.Headers.TryAddWithoutValidation("TTL", ValidadeSegundos.ToString());
        pedido.Headers.TryAddWithoutValidation("Urgency", "high");
        if (topico is not null) pedido.Headers.TryAddWithoutValidation("Topic", topico);
        pedido.Headers.TryAddWithoutValidation("Authorization", Vapid.Autorizacao(endpoint, opcoes.Value, DateTime.UtcNow));

        using var resposta = await http.SendAsync(pedido, ct);
        return resposta.StatusCode;
    }
}
