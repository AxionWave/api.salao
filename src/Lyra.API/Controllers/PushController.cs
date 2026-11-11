using Lyra.API.Auth;
using Lyra.Application.Abstractions;
using Lyra.Application.Push;
using Lyra.Core.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

/// <summary>Avisos da agenda no aparelho com o Lyra fechado (Web Push). O envio é o canal <c>push</c> da fila.</summary>
[ApiController]
[Authorize]
[RequireModulo(ModuleCodes.Agenda)]
[Route("api/salao/push")]
public sealed class PushController(IPushService push, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet("chave")]
    public ActionResult<PushChaveResponse> Chave() => Ok(push.Chave());

    [HttpPost("assinaturas")]
    public async Task<IActionResult> Assinar([FromBody] AssinarPushRequest request, CancellationToken ct)
    {
        if (!TryUsuario(out var empresaId, out var usuarioId, out var erro)) return erro;
        await push.AssinarAsync(empresaId, usuarioId, request, ct);
        return NoContent();
    }

    /// <summary>POST com corpo: o endpoint é uma URL longa, ruim de levar na rota de um DELETE.</summary>
    [HttpPost("assinaturas/remover")]
    public async Task<IActionResult> Remover([FromBody] RemoverPushRequest request, CancellationToken ct)
    {
        if (!TryUsuario(out _, out var usuarioId, out var erro)) return erro;
        await push.RemoverAsync(usuarioId, request.Endpoint ?? "", ct);
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
