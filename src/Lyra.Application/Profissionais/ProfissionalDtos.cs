namespace Lyra.Application.Profissionais;

public sealed record IntervaloDto(int DiaSemana, string Inicio, string Fim);

public sealed record ProfissionalDto(
    int Id,
    int EmpresaId,
    int? PessoaId,
    string Nome,
    string Cor,
    bool Ativo,
    IReadOnlyList<IntervaloDto> Disponibilidades,
    DateTime DataCriacao,
    DateTime? DataAtualizacao);

public sealed record SalvarProfissionalRequest(
    string Nome,
    int? PessoaId,
    string? Cor,
    bool Ativo = true,
    IReadOnlyList<IntervaloDto>? Disponibilidades = null);

/// <summary>Simula salvar: com <c>Ativo = false</c>, todos os futuros em aberto; senão, os que saem do novo expediente.</summary>
public sealed record ImpactoProfissionalRequest(
    bool Ativo = true,
    IReadOnlyList<IntervaloDto>? Disponibilidades = null);

public sealed record AgendamentoAfetadoDto(int Id, DateTime Inicio, DateTime Fim, string ClienteNome, string Status);

public sealed record ImpactoProfissionalResponse(int Total, IReadOnlyList<AgendamentoAfetadoDto> Itens);

public sealed record ListaProfissionaisResponse(
    IReadOnlyList<ProfissionalDto> Items,
    int Total,
    int Page,
    int PageSize);
