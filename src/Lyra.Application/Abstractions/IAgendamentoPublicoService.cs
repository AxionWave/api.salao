using Lyra.Application.Notificacoes;

namespace Lyra.Application.Abstractions;

/// <summary>Agendamento acessado pelo link assinado do e-mail. Token inválido = KeyNotFoundException.</summary>
public interface IAgendamentoPublicoService
{
    Task<AgendamentoPublicoDto> ObterAsync(string token, CancellationToken ct);

    /// <summary>InvalidOperationException quando já não dá para confirmar (cancelado, passado, concluído).</summary>
    Task<AgendamentoPublicoDto> ConfirmarAsync(string token, CancellationToken ct);
}
