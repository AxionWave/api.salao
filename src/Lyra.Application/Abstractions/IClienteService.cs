using Lyra.Application.Clientes;

namespace Lyra.Application.Abstractions;

public interface IClienteService
{
    Task<ListaClientesResponse> ListarAsync(int empresaId, string? busca, bool? ativo, int page, int pageSize, CancellationToken ct);
    Task<ClienteDto?> ObterAsync(int empresaId, int id, CancellationToken ct);
    Task<ClienteDto> CriarAsync(int empresaId, SalvarClienteRequest request, CancellationToken ct);
    Task<ClienteDto> AtualizarAsync(int empresaId, int id, SalvarClienteRequest request, CancellationToken ct);
    Task<ClienteDto> AlterarAtivoAsync(int empresaId, int id, bool ativo, CancellationToken ct);
    /// <summary>Membros da família do cliente, incluindo ele. Vazio se não tiver família.</summary>
    Task<IReadOnlyList<ClienteDto>> FamiliaAsync(int empresaId, int id, CancellationToken ct);
    /// <summary>Clientes que já usam este telefone ou e-mail, para oferecer a família antes de salvar.</summary>
    Task<IReadOnlyList<ClienteDto>> MesmoContatoAsync(int empresaId, string? telefone, string? email, int? ignorarId, CancellationToken ct);
}
