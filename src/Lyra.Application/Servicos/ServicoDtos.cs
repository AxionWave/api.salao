namespace Lyra.Application.Servicos;

/// <param name="ProfissionalIds">Quem atende. Vazio = todos.</param>
public sealed record ServicoDto(
    int Id,
    int EmpresaId,
    string Nome,
    int DuracaoMinutos,
    decimal Preco,
    string? Categoria,
    bool Ativo,
    DateTime DataCriacao,
    DateTime? DataAtualizacao,
    int IntervaloAposMinutos,
    IReadOnlyList<int> ProfissionalIds);

/// <param name="ProfissionalIds">Null mantém os vínculos atuais; lista vazia = todos atendem.</param>
public sealed record SalvarServicoRequest(
    string Nome,
    int DuracaoMinutos,
    decimal Preco,
    string? Categoria,
    bool Ativo = true,
    int IntervaloAposMinutos = 0,
    IReadOnlyList<int>? ProfissionalIds = null);

public sealed record AlterarAtivoRequest(bool Ativo);

public sealed record ProfissionalResumoDto(int Id, string Nome, string Cor);

public sealed record ListaServicosResponse(
    IReadOnlyList<ServicoDto> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<string> Categorias,
    IReadOnlyList<ProfissionalResumoDto> Profissionais);
