using Lyra.API.Auth;
using Lyra.Application.Abstractions;
using Lyra.Core.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

[ApiController]
[Authorize]
[Route("api/salao")]
public sealed class MeController(ICurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>Smoke: devolve claims do JWT (userId, empresaId, roles, modulos).</summary>
    [HttpGet("me")]
    [RequireModulo(ModuleCodes.Raiz, ModuleCodes.RaizLegado)]
    public IActionResult Me()
    {
        var u = currentUser.User;
        return Ok(new
        {
            product = "Lyra",
            sigla = "LYR",
            u.UserId,
            u.Username,
            u.Email,
            u.EmpresaId,
            u.Roles,
            u.Modulos
        });
    }
}
