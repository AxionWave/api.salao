namespace Lyra.Application.Push;

/// <summary>Chave pública VAPID que o navegador usa ao assinar. Nula = Web Push desligado nesta instalação.</summary>
public sealed record PushChaveResponse(string? ChavePublica);

/// <summary>A assinatura que o navegador devolve em <c>pushManager.subscribe</c> (chaves em base64url).</summary>
public sealed record AssinarPushRequest(string Endpoint, string P256dh, string Auth, string? Aparelho = null);

public sealed record RemoverPushRequest(string Endpoint);
