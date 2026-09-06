namespace Lyra.Application.Agendamentos;

public static class StatusAgendamento
{
    public const string Agendado = "agendado";
    public const string Confirmado = "confirmado";
    public const string EmAtendimento = "em_atendimento";
    public const string Concluido = "concluido";
    public const string Cancelado = "cancelado";
    public const string NoShow = "no_show";

    public static readonly string[] Todos =
    [
        Agendado, Confirmado, EmAtendimento, Concluido, Cancelado, NoShow
    ];

    public static bool Valido(string? status) =>
        status != null && Todos.Contains(status, StringComparer.OrdinalIgnoreCase);

    public static string Normalizar(string status) => status.Trim().ToLowerInvariant();

    public static bool OcupaHorario(string status) =>
        !string.Equals(status, Cancelado, StringComparison.OrdinalIgnoreCase);

    public static bool Terminal(string status) =>
        status is Concluido or Cancelado or NoShow;

    public static bool PodeTransitar(string atual, string novo)
    {
        atual = Normalizar(atual);
        novo = Normalizar(novo);
        if (atual == novo) return true;
        if (Terminal(atual)) return false;
        return Valido(novo);
    }
}
