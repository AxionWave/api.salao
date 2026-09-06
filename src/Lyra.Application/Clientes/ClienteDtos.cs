namespace Lyra.Application.Clientes;

public sealed record ClienteDto(
    int Id,
    int EmpresaId,
    string Nome,
    string Telefone,
    string? Email,
    string? Observacoes,
    bool Ativo,
    DateTime DataCriacao,
    DateTime? DataAtualizacao);

public sealed record SalvarClienteRequest(
    string Nome,
    string Telefone,
    string? Email,
    string? Observacoes,
    bool Ativo = true);

public sealed record ListaClientesResponse(
    IReadOnlyList<ClienteDto> Items,
    int Total,
    int Page,
    int PageSize);
