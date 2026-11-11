using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lyra.Infrastructure.Notificacoes.Push;

/// <summary>
/// Cabeçalho <c>Authorization: vapid</c> (RFC 8292): um JWT ES256 com o serviço de push como público,
/// assinado com a chave privada VAPID. É assim que o serviço sabe que o envio vem de quem pediu a assinatura.
/// </summary>
public static class Vapid
{
    private static readonly TimeSpan Validade = TimeSpan.FromHours(12);

    public static string Autorizacao(string endpoint, WebPushOptions opcoes, DateTime agora)
    {
        var publico = new Uri(endpoint).GetLeftPart(UriPartial.Authority);
        var expira = new DateTimeOffset(DateTime.SpecifyKind(agora, DateTimeKind.Utc)).Add(Validade).ToUnixTimeSeconds();
        var cabecalho = Base64Url.Codificar("""{"typ":"JWT","alg":"ES256"}"""u8);
        var corpo = Base64Url.Codificar(JsonSerializer.SerializeToUtf8Bytes(new { aud = publico, exp = expira, sub = opcoes.Subject }));
        var assinado = $"{cabecalho}.{corpo}";

        var chavePublica = Base64Url.Decodificar(opcoes.PublicKey);
        var parametros = CifraWebPush.ParametrosPublicos(chavePublica);
        parametros.D = Base64Url.Decodificar(opcoes.PrivateKey);
        using var ecdsa = ECDsa.Create(parametros);
        // ES256 do JWT é r ‖ s (IEEE P1363), o formato padrão do .NET.
        var assinatura = ecdsa.SignData(Encoding.ASCII.GetBytes(assinado), HashAlgorithmName.SHA256);
        return $"vapid t={assinado}.{Base64Url.Codificar(assinatura)}, k={Base64Url.Codificar(chavePublica)}";
    }

    /// <summary>Par novo de chaves VAPID (pública, privada) em base64url.</summary>
    public static (string Publica, string Privada) GerarChaves()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(true);
        return (Base64Url.Codificar(CifraWebPush.Ponto(ecdsa)), Base64Url.Codificar(p.D));
    }
}
