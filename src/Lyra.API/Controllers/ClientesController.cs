using Lyra.API.Auth;
using Lyra.Application.Abstractions;
using Lyra.Application.Clientes;
using Lyra.Application.Notificacoes;
using Lyra.Application.Servicos;
using Lyra.Core.Modules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lyra.API.Controllers;

[ApiController]
[Authorize]
[RequireModulo(ModuleCodes.Clientes)]
[Route("api/salao/clientes")]
public sealed class ClientesController(IClienteService clientes, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ListaClientesResponse>> Listar(
        [FromQuery] string? search,
        [FromQuery] bool? ativo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await clientes.ListarAsync(empresaId, search, ativo, page, pageSize, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClienteDto>> Obter(int id, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        var item = await clientes.ObterAsync(empresaId, id, ct);
        return item is null ? NotFound(new { error = "not_found", message = "Cliente não encontrado." }) : Ok(item);
    }

    [HttpGet("{id:int}/familia")]
    public async Task<ActionResult<IReadOnlyList<ClienteDto>>> Familia(int id, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        try
        {
            return Ok(await clientes.FamiliaAsync(empresaId, id, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "not_found", message = "Cliente não encontrado." });
        }
    }

    /// <summary>Mensagens enviadas (ou tentadas) a este cliente, das mais recentes para as mais antigas.</summary>
    [HttpGet("{id:int}/notificacoes")]
    public async Task<ActionResult<IReadOnlyList<NotificacaoDto>>> Notificacoes(
        int id,
        [FromServices] INotificacaoService notificacoes,
        CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        try
        {
            return Ok(await notificacoes.HistoricoClienteAsync(empresaId, id, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "not_found", message = "Cliente não encontrado." });
        }
    }

    /// <summary>Quem já usa o telefone ou o e-mail, para a tela oferecer a família antes de salvar.</summary>
    [HttpGet("mesmo-contato")]
    public async Task<ActionResult<IReadOnlyList<ClienteDto>>> MesmoContato(
        [FromQuery] string? telefone,
        [FromQuery] string? email,
        [FromQuery] int? ignorarId,
        CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        return Ok(await clientes.MesmoContatoAsync(empresaId, telefone, email, ignorarId, ct));
    }

    [HttpPost]
    public async Task<ActionResult<ClienteDto>> Criar([FromBody] SalvarClienteRequest request, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        try
        {
            var criado = await clientes.CriarAsync(empresaId, request, ct);
            return CreatedAtAction(nameof(Obter), new { id = criado.Id }, criado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "validation_error", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "conflict", message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ClienteDto>> Atualizar(int id, [FromBody] SalvarClienteRequest request, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        try
        {
            return Ok(await clientes.AtualizarAsync(empresaId, id, request, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "not_found", message = "Cliente não encontrado." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "validation_error", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = "conflict", message = ex.Message });
        }
    }

    [HttpPatch("{id:int}/ativo")]
    public async Task<ActionResult<ClienteDto>> AlterarAtivo(int id, [FromBody] AlterarAtivoRequest request, CancellationToken ct)
    {
        if (!TryEmpresa(out var empresaId, out var erro)) return erro;
        try
        {
            return Ok(await clientes.AlterarAtivoAsync(empresaId, id, request.Ativo, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "not_found", message = "Cliente não encontrado." });
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
