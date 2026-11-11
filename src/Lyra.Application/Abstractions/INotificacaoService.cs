using Lyra.Application.Notificacoes;

namespace Lyra.Application.Abstractions;

public interface INotificacaoService
{
    /// <summary>Envios mais recentes para o cliente (todos os canais). Lança <see cref="KeyNotFoundException"/> se o cliente não for da empresa.</summary>
    Task<IReadOnlyList<NotificacaoDto>> HistoricoClienteAsync(int empresaId, int clienteId, CancellationToken ct);

    /// <summary>
    /// Aplica o aviso da AW.Mensageria à notificação de origem. Lança <see cref="KeyNotFoundException"/>
    /// se a chave não for de uma notificação desta empresa enviada por ela.
    /// </summary>
    Task AplicarAvisoMensageriaAsync(int empresaId, AvisoMensageriaDto aviso, CancellationToken ct);
}
