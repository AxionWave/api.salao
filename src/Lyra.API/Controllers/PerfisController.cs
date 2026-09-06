using Lyra.API.Auth;
using Lyra.Application.Abstractions;
using Lyra.Application.Perfis;
using Lyra.Core.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

[ApiController]
[Authorize]
[RequireModulo(ModuleCodes.Profissionais, ModuleCodes.Configuracoes, ModuleCodes.Raiz, ModuleCodes.RaizLegado)]
[Route("api/salao/perfis-acesso")]
public sealed class PerfisController(IPerfilLyraService perfis, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PerfilAcessoLyraDto>>> Listar(CancellationToken ct)
    {
        var user = currentUser.User;
        if (user.EmpresaId is not int empresaId || empresaId <= 0)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "tenant_required",
                message = "O token não tem empresaId. Faça login novamente."
            });
        }

        return Ok(await perfis.ListarAsync(empresaId, ct));
    }
}
