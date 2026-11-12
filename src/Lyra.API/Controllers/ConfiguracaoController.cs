using Lyra.API.Auth;
using Lyra.Application.Abstractions;
using Lyra.Application.Configuracao;
using Lyra.Application.Portal;
using Lyra.Core.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

[ApiController]
[Authorize]
[Route("api/salao/configuracao")]
public sealed class ConfiguracaoController(
    IConfiguracaoEmpresaService configuracao,
    IAvisosService avisos,
    IPortalConfigService portal,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>Segmento, fuso e terminologia da empresa. Todo usuário do Lyra precisa para montar a tela.</summary>
    [HttpGet]
    [RequireModulo(ModuleCodes.Raiz, ModuleCodes.RaizLegado)]
    public async Task<ActionResult<ConfiguracaoEmpresaDto>> Obter(CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await configuracao.ObterAsync(empresaId, ct));
    }

    [HttpGet("opcoes")]
    [RequireModulo(ModuleCodes.Configuracoes)]
    public ActionResult<OpcoesConfiguracaoResponse> Opcoes() =>
        Ok(new OpcoesConfiguracaoResponse(Segmentos.Todos, Segmentos.FusosHorarios));

    [HttpPut]
    [RequireModulo(ModuleCodes.Configuracoes)]
    public async Task<ActionResult<ConfiguracaoEmpresaDto>> Salvar([FromBody] SalvarConfiguracaoRequest request, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        try
        {
            return Ok(await configuracao.SalvarAsync(empresaId, request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "validation_error", message = ex.Message });
        }
    }

    /// <summary>Avisos por evento e canal, textos dos e-mails e os modelos e variáveis para montar a tela.</summary>
    [HttpGet("avisos")]
    [RequireModulo(ModuleCodes.Configuracoes)]
    public async Task<ActionResult<AvisosResponse>> ObterAvisos(CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await avisos.ObterAsync(empresaId, ct));
    }

    [HttpPut("avisos")]
    [RequireModulo(ModuleCodes.Configuracoes)]
    public async Task<ActionResult<AvisosResponse>> SalvarAvisos([FromBody] AvisosDto request, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        try
        {
            return Ok(await avisos.SalvarAsync(empresaId, request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "validation_error", message = ex.Message });
        }
    }

    /// <summary>O e-mail como sairia com estes textos, com um horário de exemplo e os dados da empresa.</summary>
    [HttpPost("avisos/previa")]
    [RequireModulo(ModuleCodes.Configuracoes)]
    public async Task<ActionResult<PreviaAvisoResponse>> PreviaAviso([FromBody] PreviaAvisoRequest request, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        try
        {
            return Ok(await avisos.PreviaAsync(empresaId, request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "validation_error", message = ex.Message });
        }
    }

    /// <summary>Agendamento online: endereço da página e regras.</summary>
    [HttpGet("portal")]
    [RequireModulo(ModuleCodes.Configuracoes)]
    public async Task<ActionResult<PortalConfigResponse>> ObterPortal(CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await portal.ObterAsync(empresaId, ct));
    }

    [HttpPut("portal")]
    [RequireModulo(ModuleCodes.Configuracoes)]
    public async Task<ActionResult<PortalConfigResponse>> SalvarPortal([FromBody] PortalConfigDto request, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await portal.SalvarAsync(empresaId, request, ct));
    }

    [HttpGet("portal/disponivel")]
    [RequireModulo(ModuleCodes.Configuracoes)]
    public async Task<ActionResult<EnderecoDisponivelResponse>> EnderecoDisponivel([FromQuery] string? endereco, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await portal.DisponivelAsync(empresaId, endereco, ct));
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
