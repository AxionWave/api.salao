namespace Lyra.Infrastructure.Notificacoes.Push;

/// <summary>Chaves VAPID (P-256, base64url): a pública vai para o navegador, a privada assina cada envio.</summary>
public sealed class WebPushOptions
{
    public const string Secao = "WebPush";

    /// <summary>Ponto público não comprimido (65 bytes).</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>Escalar privado (32 bytes). Segredo.</summary>
    public string PrivateKey { get; set; } = "";

    /// <summary>Contato que o serviço de push usa se houver problema com os envios.</summary>
    public string Subject { get; set; } = "mailto:contato@lumensoft.com.br";

    public bool Configurado => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
