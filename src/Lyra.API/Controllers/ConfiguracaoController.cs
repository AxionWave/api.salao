using Lyra.API.Auth;
using Lyra.Application.Abstractions;
using Lyra.Application.Configuracao;
using Lyra.Core.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

[ApiController]
[Authorize]
[Route("api/salao/configuracao")]
public sealed class ConfiguracaoController(IConfiguracaoEmpresaService configuracao, ICurrentUserAccessor currentUser) : ControllerBase
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
