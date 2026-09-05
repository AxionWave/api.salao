namespace Lyra.Application.Servicos;

public sealed record ServicoDto(
    int Id,
    int EmpresaId,
    string Nome,
    int DuracaoMinutos,
    decimal Preco,
    string? Categoria,
    bool Ativo,
    DateTime DataCriacao,
    DateTime? DataAtualizacao);

public sealed record SalvarServicoRequest(
    string Nome,
    int DuracaoMinutos,
    decimal Preco,
    string? Categoria,
    bool Ativo = true);

public sealed record AlterarAtivoRequest(bool Ativo);

public sealed record ListaServicosResponse(
    IReadOnlyList<ServicoDto> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<string> Categorias);
