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

    [HttpGet("conta/preferencias")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<PreferenciasClienteDto>> Preferencias(string endereco, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        return await Tratar(() => portal.PreferenciasAsync(contaId, endereco, ct));
    }

    [HttpPut("conta/preferencias")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<PreferenciasClienteDto>> SalvarPreferencias(
        string endereco, [FromBody] SalvarPreferenciasRequest request, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        return await Tratar(() => portal.SalvarPreferenciasAsync(contaId, endereco, request, ct));
    }

    /// <summary>Quem o cliente pode agendar além dele (família montada pela empresa). Vazio na maioria das contas.</summary>
    [HttpGet("conta/familia")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<IReadOnlyList<PessoaFamiliaDto>>> Familia(string endereco, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        return await Tratar(() => portal.FamiliaAsync(contaId, endereco, ct));
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

    /// <summary>Próximos horários e histórico do cliente nesta empresa.</summary>
    [HttpGet("meus-agendamentos")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<MeusAgendamentosDto>> MeusAgendamentos(string endereco, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        return await Tratar(() => portal.MeusAgendamentosAsync(contaId, endereco, ct));
    }

    /// <summary>Horários livres para remarcar: mesmos serviços, e o próprio horário atual não conta como ocupado.</summary>
    [HttpGet("meus-agendamentos/{id:int}/horarios")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<HorariosLivresResponse>> HorariosRemarcar(
        string endereco, int id, [FromQuery] DateOnly data, [FromQuery] int? profissionalId, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        if (data == default)
        {
            return BadRequest(new { error = "validation_error", message = "Informe a data (AAAA-MM-DD)." });
        }
        return await Tratar(() => portal.HorariosRemarcarAsync(contaId, endereco, id, data, profissionalId, ct));
    }

    [HttpPost("meus-agendamentos/{id:int}/cancelar")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<AgendamentoClienteDto>> Cancelar(
        string endereco, int id, [FromBody] CancelarPortalRequest request, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        return await Tratar(() => portal.CancelarAsync(contaId, endereco, id, request, ct));
    }

    /// <summary>Devolve o novo horário; o anterior fica cancelado como "Remarcado".</summary>
    [HttpPost("meus-agendamentos/{id:int}/remarcar")]
    [Authorize(AuthenticationSchemes = TokensCliente.Esquema)]
    public async Task<ActionResult<AgendamentoClienteDto>> Remarcar(
        string endereco, int id, [FromBody] RemarcarPortalRequest request, CancellationToken ct)
    {
        if (TokensCliente.ContaId(User) is not int contaId) return Unauthorized();
        return await Tratar(() => portal.RemarcarAsync(contaId, endereco, id, request, ct));
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
