using System.Security.Cryptography;
using System.Text;
using Lyra.Application.Abstractions;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Notificacoes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Lyra.API.Controllers;

/// <summary>Chamadas de outros serviços na rede interna, autenticadas pelo token interno (sem JWT).</summary>
[ApiController]
[AllowAnonymous]
[Route("api/salao/internos")]
public sealed class InternosController(INotificacaoService notificacoes, IOptions<EmailOptions> email) : ControllerBase
{
    /// <summary>A AW.Mensageria conta o que aconteceu com um e-mail depois de aceito (entregue, devolvido, spam…).</summary>
    [HttpPost("mensageria/envios")]
    public async Task<IActionResult> AvisoMensageria([FromBody] AvisoMensageriaDto aviso, CancellationToken ct)
    {
        if (!TokenValido()) return Unauthorized(new { error = "unauthorized", message = "Token interno inválido." });
        if (!int.TryParse(Request.Headers["X-Empresa-Id"].FirstOrDefault(), out var empresaId))
            return BadRequest(new { error = "validation", message = "Informe a empresa em X-Empresa-Id." });
        try
        {
            await notificacoes.AplicarAvisoMensageriaAsync(empresaId, aviso, ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
    }

    private bool TokenValido()
    {
        var esperado = email.Value.TokenInterno;
        var recebido = Request.Headers["X-Internal-Service-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(esperado) || string.IsNullOrEmpty(recebido)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebido), Encoding.UTF8.GetBytes(esperado));
    }
}
