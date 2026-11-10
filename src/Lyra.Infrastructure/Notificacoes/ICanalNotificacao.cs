using Lyra.Infrastructure.Persistence.Entities;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>Um meio de entrega (e-mail, WhatsApp, painel). Cada provedor registra o seu.</summary>
public interface ICanalNotificacao
{
    /// <summary>Nome usado em <c>Notificacoes:Canais</c> e gravado na notificação.</summary>
    string Nome { get; }

    /// <summary>Se este evento vira mensagem neste canal para a empresa (ex.: a empresa ligou o envio).</summary>
    bool Aceita(string evento, string? origem, EmpresaConfig? config) => true;

    /// <summary>Para onde mandar a este cliente; null quando ele não tem como receber neste canal.</summary>
    string? Destino(Cliente cliente);

    /// <summary>
    /// Envia e devolve o id da mensagem no provedor. Exceção = falha, e o worker tenta de novo;
    /// <see cref="NotificacaoRecusadaException"/> = não adianta tentar de novo.
    /// </summary>
    Task<string?> EnviarAsync(Notificacao notificacao, CancellationToken ct);
}

/// <summary>O provedor recusou a mensagem (destino inválido, conteúdo rejeitado): falha sem novas tentativas.</summary>
public sealed class NotificacaoRecusadaException(string message) : Exception(message);
