using Lyra.Application.Agendamentos;

namespace Lyra.Application.Abstractions;

public interface IAgendamentoService
{
    Task<ListaAgendamentosResponse> ListarAsync(
        int empresaId,
        DateTime de,
        DateTime ate,
        int? profissionalId,
        string? status,
        CancellationToken ct);

    Task<OpcoesAgendaResponse> OpcoesAsync(int empresaId, CancellationToken ct);

    Task<AgendamentoDto?> ObterAsync(int empresaId, int id, CancellationToken ct);

    /// <param name="origem">"cliente" quando foi marcado pelo portal: os avisos não têm autor da equipe.</param>
    /// <param name="contaClienteId">Conta do portal que marcou.</param>
    Task<AgendamentoDto> CriarAsync(
        int empresaId, SalvarAgendamentoRequest request, CancellationToken ct, string? origem = null, int? contaClienteId = null,
        bool confirmado = false);

    Task<AgendamentoDto> AtualizarAsync(int empresaId, int id, SalvarAgendamentoRequest request, CancellationToken ct);

    /// <param name="origem">"cliente" quando o próprio cliente cancelou pelo portal.</param>
    Task<AgendamentoDto> AlterarStatusAsync(
        int empresaId, int id, string status, string? motivo, CancellationToken ct, string? origem = null);

    /// <summary>
    /// Marca um novo horário para o mesmo cliente. Se o original ainda estava em aberto, é cancelado
    /// junto; se foi falta ou cancelamento, fica como está. O novo herda a conta do portal. Devolve o novo agendamento.
    /// </summary>
    /// <param name="origem">"cliente" quando o próprio cliente remarcou pelo portal.</param>
    Task<AgendamentoDto> RemarcarAsync(
        int empresaId, int id, SalvarAgendamentoRequest request, CancellationToken ct, string? origem = null,
        bool confirmado = false);

    Task<HorariosLivresResponse> HorariosLivresAsync(int empresaId, HorariosLivresRequest request, CancellationToken ct);
}
