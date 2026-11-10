namespace Lyra.Application.Notificacoes;

public static class EventosNotificacao
{
    public const string AgendamentoCriado = "agendamento_criado";
    public const string AgendamentoRemarcado = "agendamento_remarcado";
    public const string AgendamentoCancelado = "agendamento_cancelado";
    public const string AgendamentoConfirmado = "agendamento_confirmado";
}

public static class StatusNotificacao
{
    public const string Pendente = "pendente";
    public const string Enviado = "enviado";
    public const string Falhou = "falhou";
}

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
