using Lyra.API.Auth;
using Lyra.Application.Abstractions;
using Lyra.Application.Painel;
using Lyra.Core.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

/// <summary>O sino do painel. Ao vivo, os avisos chegam pelo hub <c>/api/salao/hub/painel</c>.</summary>
[ApiController]
[Authorize]
[RequireModulo(ModuleCodes.Agenda)]
[Route("api/salao/painel/notificacoes")]
public sealed class PainelController(IPainelService painel, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PainelNotificacoesResponse>> Listar(CancellationToken ct)
    {
        if (!TryUsuario(out var empresaId, out var usuarioId, out var erro)) return erro;
        return Ok(await painel.ListarAsync(empresaId, usuarioId, ct));
    }

    [HttpPost("vistas")]
    public async Task<IActionResult> MarcarVistas(CancellationToken ct)
    {
        if (!TryUsuario(out var empresaId, out var usuarioId, out var erro)) return erro;
        await painel.MarcarVistasAsync(empresaId, usuarioId, ct);
        return NoContent();
    }

    private bool TryUsuario(out int empresaId, out int usuarioId, out ActionResult erro)
    {
        var user = currentUser.User;
        empresaId = user.EmpresaId ?? 0;
        usuarioId = user.UserId ?? 0;
        erro = BadRequest(new { error = "validation_error", message = "Token sem empresa ou usuário." });
        return empresaId > 0 && usuarioId > 0;
    }
}
