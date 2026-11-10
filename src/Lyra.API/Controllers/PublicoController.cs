using Lyra.Application.Abstractions;
using Lyra.Application.Notificacoes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

/// <summary>Rotas sem login, abertas pelo link assinado que o cliente recebe por e-mail.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/salao/publico/agendamentos")]
public sealed class PublicoController(IAgendamentoPublicoService agendamentos) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ActionResult<AgendamentoPublicoDto>> Obter(string token, CancellationToken ct)
    {
        try
        {
            return Ok(await agendamentos.ObterAsync(token, ct));
        }
        catch (KeyNotFoundException)
        {
            return NaoEncontrado();
        }
    }

    [HttpPost("{token}/confirmar")]
    public async Task<ActionResult<AgendamentoPublicoDto>> Confirmar(string token, CancellationToken ct)
    {
        try
        {
            return Ok(await agendamentos.ConfirmarAsync(token, ct));
        }
        catch (KeyNotFoundException)
        {
            return NaoEncontrado();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "conflict", message = ex.Message });
        }
    }

    private NotFoundObjectResult NaoEncontrado() =>
        NotFound(new { error = "not_found", message = "Link inválido ou agendamento não encontrado." });
}
