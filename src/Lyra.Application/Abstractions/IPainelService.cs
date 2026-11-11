using Lyra.Application.Painel;

namespace Lyra.Application.Abstractions;

/// <summary>O sino do painel: avisos recentes da agenda para a equipe da empresa.</summary>
public interface IPainelService
{
    /// <summary>Os avisos mais recentes, sem os que o próprio usuário causou, e quantos ele ainda não viu.</summary>
    Task<PainelNotificacoesResponse> ListarAsync(int empresaId, int usuarioId, CancellationToken ct);

    /// <summary>Marca tudo até agora como visto por este usuário.</summary>
    Task MarcarVistasAsync(int empresaId, int usuarioId, CancellationToken ct);
}
