namespace Lyra.Application.Notificacoes;

public static class EventosNotificacao
{
    public const string AgendamentoCriado = "agendamento_criado";
    public const string AgendamentoRemarcado = "agendamento_remarcado";
    public const string AgendamentoCancelado = "agendamento_cancelado";
    public const string AgendamentoConfirmado = "agendamento_confirmado";
    /// <summary>Programado: algumas horas antes do início (<c>lembretes_horas</c> da empresa).</summary>
    public const string AgendamentoLembrete = "agendamento_lembrete";
    /// <summary>Programado: a agenda do dia para o profissional, de manhã. Não tem cliente nem agendamento.</summary>
    public const string ResumoDia = "resumo_dia";
}

public static class StatusNotificacao
{
    public const string Pendente = "pendente";
    public const string Enviado = "enviado";
    /// <summary>O provedor confirmou a entrega (aviso da AW.Mensageria).</summary>
    public const string Entregue = "entregue";
    public const string Falhou = "falhou";
}

/// <summary>
/// O que a AW.Mensageria conta depois que aceitou o envio. <c>Evento</c>: entregue, devolvido, reclamacao,
/// falhou ou descadastrado; <c>Status</c> é o estado atual do envio lá.
/// </summary>
public sealed record AvisoMensageriaDto(
    string Id,
    string Chave,
    string? Referencia,
    string? Canal,
    string Evento,
    string? Status,
    string? Erro,
    DateTime? Data);

public sealed record NotificacaoDto(
    long Id,
    string Evento,
    string Canal,
    string? Destino,
    int? AgendamentoId,
    string Status,
    int Tentativas,
    string? UltimoErro,
    DateTime DataCriacao,
    DateTime? DataEnvio);
