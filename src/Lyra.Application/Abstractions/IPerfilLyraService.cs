using Lyra.Application.Perfis;

namespace Lyra.Application.Abstractions;

public interface IPerfilLyraService
{
    Task<IReadOnlyList<PerfilAcessoLyraDto>> ListarAsync(int empresaId, CancellationToken ct);
}
