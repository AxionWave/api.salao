using Lyra.Application.Painel;
using Lyra.Infrastructure.Notificacoes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Lyra.Infrastructure.AoVivo;

/// <summary>
/// Avisa as telas abertas da empresa. É "melhor esforço": quem estava desconectado recarrega ao voltar,
/// e o sino lê do banco. Chamar só depois do commit, senão a tela recarrega antes da mudança existir.
/// </summary>
public sealed class AvisosAoVivo(IHubContext<PainelHub> hub, SinalNotificacoes sinal, ILogger<AvisosAoVivo> logger)
{
    public const string EventoAgenda = "agendaAlterada";
    public const string EventoNotificacao = "notificacao";

    /// <summary>A agenda da empresa mudou (agendamento ou bloqueio). Também acorda a fila de notificações.</summary>
    public async Task AgendaAlteradaAsync(int empresaId, int? profissionalId = null)
    {
        sinal.Acordar();
        try
        {
            await hub.Clients.Group(PainelHub.GrupoEmpresa(empresaId))
                .SendAsync(EventoAgenda, new { profissionalId }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao avisar a agenda ao vivo da empresa {EmpresaId}", empresaId);
        }
    }

    public Task NotificacaoAsync(int empresaId, PainelNotificacaoDto aviso, CancellationToken ct) =>
        hub.Clients.Group(PainelHub.GrupoEmpresa(empresaId)).SendAsync(EventoNotificacao, aviso, ct);
}
