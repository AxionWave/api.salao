namespace Lyra.Application.Bloqueios;

/// <summary>ProfissionalId nulo = bloqueio da empresa toda.</summary>
public sealed record BloqueioDto(
    int Id,
    int? ProfissionalId,
    string? ProfissionalNome,
    DateTime Inicio,
    DateTime Fim,
    string? Motivo,
    DateTime DataCriacao);

public sealed record SalvarBloqueioRequest(
    int? ProfissionalId,
    DateTime Inicio,
    DateTime Fim,
    string? Motivo);

/// <summary>AgendamentosNoPeriodo: quantos agendamentos ativos já caem no período (não são cancelados automaticamente).</summary>
public sealed record CriarBloqueioResponse(BloqueioDto Bloqueio, int AgendamentosNoPeriodo);

public sealed record ListaBloqueiosResponse(IReadOnlyList<BloqueioDto> Items);
