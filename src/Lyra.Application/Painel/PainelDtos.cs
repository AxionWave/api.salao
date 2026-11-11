namespace Lyra.Application.Painel;

/// <summary>Um aviso do sino: o que aconteceu na agenda, congelado no momento do evento.</summary>
/// <param name="Origem"><c>cliente</c> quando o próprio cliente fez (link do e-mail); null quando foi a equipe.</param>
/// <param name="AutorId">Usuário da equipe que fez a mudança.</param>
public sealed record PainelNotificacaoDto(
    long Id,
    string Evento,
    int? AgendamentoId,
    string Cliente,
    string Profissional,
    DateTime Inicio,
    string InicioLocal,
    string? InicioAnteriorLocal,
    string? Motivo,
    string? Origem,
    int? AutorId,
    DateTime Data);

public sealed record PainelNotificacoesResponse(IReadOnlyList<PainelNotificacaoDto> Itens, int NaoLidas);
