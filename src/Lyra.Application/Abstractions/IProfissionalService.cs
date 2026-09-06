using Lyra.Application.Profissionais;

namespace Lyra.Application.Abstractions;

public interface IProfissionalService
{
    Task<ListaProfissionaisResponse> ListarAsync(int empresaId, string? busca, bool? ativo, int page, int pageSize, CancellationToken ct);
    Task<ProfissionalDto?> ObterAsync(int empresaId, int id, CancellationToken ct);
    Task<ProfissionalDto> CriarAsync(int empresaId, SalvarProfissionalRequest request, CancellationToken ct);
    Task<ProfissionalDto> AtualizarAsync(int empresaId, int id, SalvarProfissionalRequest request, CancellationToken ct);
    Task<ProfissionalDto> AlterarAtivoAsync(int empresaId, int id, bool ativo, CancellationToken ct);
}
