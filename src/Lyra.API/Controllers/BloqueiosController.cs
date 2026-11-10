using Lyra.API.Auth;
using Lyra.Application.Abstractions;
using Lyra.Application.Bloqueios;
using Lyra.Core.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

[ApiController]
[Authorize]
[RequireModulo(ModuleCodes.Agenda)]
[Route("api/salao/bloqueios")]
public sealed class BloqueiosController(IBloqueioService bloqueios, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ListaBloqueiosResponse>> Listar(
        [FromQuery] DateTime de,
        [FromQuery] DateTime ate,
        [FromQuery] int? profissionalId,
        CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        if (de == default || ate == default)
        {
            return BadRequest(new { error = "validation_error", message = "Informe o período (de e ate)." });
        }
        return Ok(await bloqueios.ListarAsync(empresaId, de, ate, profissionalId, ct));
    }

    [HttpPost]
    public async Task<ActionResult<CriarBloqueioResponse>> Criar([FromBody] SalvarBloqueioRequest request, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        var criado = await bloqueios.CriarAsync(empresaId, request, ct);
        return StatusCode(StatusCodes.Status201Created, criado);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        await bloqueios.ExcluirAsync(empresaId, id, ct);
        return NoContent();
    }

    private bool TryEmpresa(out int empresaId, out ActionResult erro)
    {
        var user = currentUser.User;
        if (user.EmpresaId is int id && id > 0)
        {
            empresaId = id;
            erro = null!;
            return true;
        }

        empresaId = 0;
        erro = StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = "tenant_required",
            message = "O token não tem empresaId. Faça login novamente."
        });
        return false;
    }
}
