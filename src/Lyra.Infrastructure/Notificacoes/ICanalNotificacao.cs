using Lyra.Infrastructure.Persistence.Entities;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>Um meio de entrega (e-mail, WhatsApp, painel). Cada provedor registra o seu.</summary>
public interface ICanalNotificacao
{
    /// <summary>Nome usado em <c>Notificacoes:Canais</c> e gravado na notificação.</summary>
    string Nome { get; }

    /// <summary>Para onde mandar a este cliente; null quando ele não tem como receber neste canal.</summary>
    string? Destino(Cliente cliente);

    /// <summary>Envia e devolve o id da mensagem no provedor. Exceção = falha, e o worker tenta de novo.</summary>
    Task<string?> EnviarAsync(Notificacao notificacao, CancellationToken ct);
}
