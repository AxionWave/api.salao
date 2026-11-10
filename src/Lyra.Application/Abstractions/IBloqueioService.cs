using Lyra.Application.Bloqueios;

namespace Lyra.Application.Abstractions;

public interface IBloqueioService
{
    Task<ListaBloqueiosResponse> ListarAsync(int empresaId, DateTime de, DateTime ate, int? profissionalId, CancellationToken ct);
    Task<CriarBloqueioResponse> CriarAsync(int empresaId, SalvarBloqueioRequest request, CancellationToken ct);
    Task ExcluirAsync(int empresaId, int id, CancellationToken ct);
}
