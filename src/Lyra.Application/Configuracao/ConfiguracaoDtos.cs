namespace Lyra.Application.Configuracao;

/// <summary>Rótulo exibido no front. Genero: "m" ou "f" (Novo/Nova, Nenhum/Nenhuma).</summary>
public sealed record TermoDto(string Singular, string Plural, string Genero);

public sealed record TerminologiaDto(
    TermoDto Recurso,
    TermoDto Servico,
    TermoDto Cliente,
    TermoDto Agendamento);

public sealed record SegmentoDto(string Id, string Nome, TerminologiaDto Terminologia);

public sealed record FusoHorarioDto(string Id, string Nome);

public sealed record ConfiguracaoEmpresaDto(
    string Segmento,
    string FusoHorario,
    TerminologiaDto Terminologia,
    bool Personalizada,
    DateTime? DataAtualizacao,
    bool EnviarEmails = false,
    IReadOnlyList<int>? LembretesHoras = null,
    bool ResumoDiario = false);

/// <summary>
/// Terminologia nula = usa a do segmento. EnviarEmails, LembretesHoras e ResumoDiario nulos = mantêm o atual.
/// LembretesHoras: horas antes do início (1 a 72, até 3), vazio = sem lembrete.
/// </summary>
public sealed record SalvarConfiguracaoRequest(
    string Segmento,
    string FusoHorario,
    TerminologiaDto? Terminologia,
    bool? EnviarEmails = null,
    IReadOnlyList<int>? LembretesHoras = null,
    bool? ResumoDiario = null);

public sealed record OpcoesConfiguracaoResponse(
    IReadOnlyList<SegmentoDto> Segmentos,
    IReadOnlyList<FusoHorarioDto> FusosHorarios);
