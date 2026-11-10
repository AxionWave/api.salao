namespace Lyra.Application.Clientes;

public sealed record ClienteDto(
    int Id,
    int EmpresaId,
    string Nome,
    string Telefone,
    string? Email,
    string? Observacoes,
    bool Ativo,
    int? FamiliaId,
    string? FamiliaNome,
    string? Parentesco,
    DateTime DataCriacao,
    DateTime? DataAtualizacao);

/// <param name="FamiliaDeClienteId">Entra na família deste cliente (cria a família se ele ainda não tiver).</param>
/// <param name="SairDaFamilia">Tira o cliente da família atual.</param>
public sealed record SalvarClienteRequest(
    string Nome,
    string Telefone,
    string? Email,
    string? Observacoes,
    bool Ativo = true,
    string? Parentesco = null,
    int? FamiliaDeClienteId = null,
    bool SairDaFamilia = false);

public sealed record ListaClientesResponse(
    IReadOnlyList<ClienteDto> Items,
    int Total,
    int Page,
    int PageSize);
