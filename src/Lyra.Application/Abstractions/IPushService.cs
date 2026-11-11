using Lyra.Application.Push;

namespace Lyra.Application.Abstractions;

/// <summary>Aparelhos da equipe que recebem os avisos da agenda com o Lyra fechado (Web Push).</summary>
public interface IPushService
{
    PushChaveResponse Chave();

    /// <summary>Guarda (ou passa para este usuário) a assinatura do aparelho. Endpoint fora dos serviços de push conhecidos = ArgumentException.</summary>
    Task AssinarAsync(int empresaId, int usuarioId, AssinarPushRequest request, CancellationToken ct);

    /// <summary>Remove a assinatura do aparelho, se for deste usuário.</summary>
    Task RemoverAsync(int usuarioId, string endpoint, CancellationToken ct);
}
