using Lyra.API.Auth;
using Lyra.Application.Abstractions;
using Lyra.Application.Agendamentos;
using Lyra.Core.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

[ApiController]
[Authorize]
[RequireModulo(ModuleCodes.Agenda)]
[Route("api/salao/agendamentos")]
public sealed class AgendamentosController(IAgendamentoService agendamentos, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ListaAgendamentosResponse>> Listar(
        [FromQuery] DateTime de,
        [FromQuery] DateTime ate,
        [FromQuery] int? profissionalId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        if (de == default || ate == default)
        {
            return BadRequest(new { error = "validation_error", message = "Informe o período (de e ate)." });
        }
        return Ok(await agendamentos.ListarAsync(empresaId, de, ate, profissionalId, status, ct));
    }

    [HttpGet("opcoes")]
    public async Task<ActionResult<OpcoesAgendaResponse>> Opcoes(CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await agendamentos.OpcoesAsync(empresaId, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AgendamentoDto>> Obter(int id, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        var item = await agendamentos.ObterAsync(empresaId, id, ct);
        return item is null
            ? NotFound(new { error = "not_found", message = "Agendamento não encontrado." })
            : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<AgendamentoDto>> Criar([FromBody] SalvarAgendamentoRequest request, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        var criado = await agendamentos.CriarAsync(empresaId, request, ct);
        return CreatedAtAction(nameof(Obter), new { id = criado.Id }, criado);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AgendamentoDto>> Atualizar(
        int id,
        [FromBody] SalvarAgendamentoRequest request,
        CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await agendamentos.AtualizarAsync(empresaId, id, request, ct));
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<AgendamentoDto>> AlterarStatus(
        int id,
        [FromBody] AlterarStatusRequest request,
        CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await agendamentos.AlterarStatusAsync(empresaId, id, request.Status, ct));
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
