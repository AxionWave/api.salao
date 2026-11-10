using Lyra.Application.Configuracao;

namespace Lyra.Application.Abstractions;

public interface IConfiguracaoEmpresaService
{
    Task<ConfiguracaoEmpresaDto> ObterAsync(int empresaId, CancellationToken ct);
    Task<ConfiguracaoEmpresaDto> SalvarAsync(int empresaId, SalvarConfiguracaoRequest request, CancellationToken ct);
}
