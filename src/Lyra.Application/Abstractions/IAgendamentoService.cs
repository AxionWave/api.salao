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

    Task<AgendamentoDto> CriarAsync(int empresaId, SalvarAgendamentoRequest request, CancellationToken ct);

    Task<AgendamentoDto> AtualizarAsync(int empresaId, int id, SalvarAgendamentoRequest request, CancellationToken ct);

    Task<AgendamentoDto> AlterarStatusAsync(int empresaId, int id, string status, string? motivo, CancellationToken ct);

    /// <summary>
    /// Marca um novo horário para o mesmo cliente. Se o original ainda estava em aberto, é cancelado
    /// junto; se foi falta ou cancelamento, fica como está. Devolve o novo agendamento.
    /// </summary>
    Task<AgendamentoDto> RemarcarAsync(int empresaId, int id, SalvarAgendamentoRequest request, CancellationToken ct);

    Task<HorariosLivresResponse> HorariosLivresAsync(int empresaId, HorariosLivresRequest request, CancellationToken ct);
}
