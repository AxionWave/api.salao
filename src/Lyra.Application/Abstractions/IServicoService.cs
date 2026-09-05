using Lyra.Application.Servicos;

namespace Lyra.Application.Abstractions;

public interface IServicoService
{
    Task<ListaServicosResponse> ListarAsync(int empresaId, string? busca, bool? ativo, int page, int pageSize, CancellationToken ct);
    Task<ServicoDto?> ObterAsync(int empresaId, int id, CancellationToken ct);
    Task<ServicoDto> CriarAsync(int empresaId, SalvarServicoRequest request, CancellationToken ct);
    Task<ServicoDto> AtualizarAsync(int empresaId, int id, SalvarServicoRequest request, CancellationToken ct);
    Task<ServicoDto> AlterarAtivoAsync(int empresaId, int id, bool ativo, CancellationToken ct);
}
