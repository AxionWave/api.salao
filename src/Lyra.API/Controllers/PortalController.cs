using Lyra.API.Auth;
using Lyra.Application.Agendamentos;
using Lyra.Application.Portal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

/// <summary>
/// Página pública da empresa (<c>lyra.lumensoft.com.br/{endereco}</c>). Ver e pedir código é aberto;
/// conta e agendar exigem o token do cliente, que é outro esquema e não vale nas rotas da equipe.
/// </summary>
[ApiController]
[Route("api/salao/publico/portal/{endereco}")]
public sealed class PortalController(IPortalService portal) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginaPortalDto>> Pagina(string endereco, CancellationToken ct) =>
        await Tratar(() => portal.PaginaAsync(endereco, ct));

    [HttpGet("horarios")]
    public async Task<ActionResult<HorariosLivresResponse>> Horarios(
        string endereco,
        [FromQuery] DateOnly data,
        [FromQuery] int[] servicoIds,
        [FromQuery] int? profissionalId,
        CancellationToken ct)
    {
        if (data == default)
        {
            return BadRequest(new { error = "validation_error", message = "Informe a data (AAAA-MM-DD)." });
        }
        return await Tratar(() => portal.HorariosAsync(endereco, data, servicoIds, profissionalId, ct));
    }

    /// <summary>Manda o código de 6 dígitos para o e-mail. 429 quando pedido demais.</summary>
    [HttpPost("codigo")]
    public async Task<ActionResult> PedirCodigo(string endereco, [FromBody] PedirCodigoRequest request, CancellationToken ct)
    {
        try
        {
            await portal.PedirCodigoAsync(endereco, request.Email, Ip(), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NaoEncontrado(ex);
        }
    }

    [HttpPost("entrar")]
    public async Task<ActionResult<SessaoClienteDto>> Entrar(string endereco, [FromBody] EntrarRequest request, CancellationToken ct) =>
        await Tratar(() => portal.EntrarAsync(endereco, request.Email, request.Codigo, ct));

    [HttpGet("conta")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<ContaClienteDto>> Conta(string endereco, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        return await Tratar(() => portal.ContaAsync(contaId, endereco, ct));
    }

    [HttpPut("conta")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<ContaClienteDto>> SalvarConta(string endereco, [FromBody] SalvarContaRequest request, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        return await Tratar(() => portal.SalvarContaAsync(contaId, endereco, request, ct));
    }

    [HttpPost("agendamentos")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<AgendamentoPortalDto>> Agendar(string endereco, [FromBody] AgendarPortalRequest request, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        try
        {
            var criado = await portal.AgendarAsync(contaId, endereco, request, ct);
            return StatusCode(StatusCodes.Status201Created, criado);
        }
        catch (KeyNotFoundException ex)
        {
            return NaoEncontrado(ex);
        }
    }

    private async Task<ActionResult<T>> Tratar<T>(Func<Task<T>> acao)
    {
        try
        {
            return Ok(await acao());
        }
        catch (KeyNotFoundException ex)
        {
            return NaoEncontrado(ex);
        }
    }

    private NotFoundObjectResult NaoEncontrado(KeyNotFoundException ex) =>
        NotFound(new { error = "not_found", message = ex.Message });

    /// <summary>O Gateway repassa o IP do cliente no primeiro item do X-Forwarded-For.</summary>
    private string? Ip()
    {
        var encaminhado = Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
        var ip = encaminhado.Length > 0 ? encaminhado : HttpContext.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrEmpty(ip) ? null : ip.Length > 64 ? ip[..64] : ip;
    }
}
