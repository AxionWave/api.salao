namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>
/// Uma mensagem para um destino num canal. É a fila (outbox) e o histórico ao mesmo tempo:
/// nasce pendente na mesma transação do agendamento e o worker atualiza o resultado.
/// </summary>
public sealed class Notificacao
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public string Evento { get; set; } = string.Empty;
    public string Canal { get; set; } = string.Empty;
    /// <summary>E-mail ou telefone no momento do evento; null nos canais da equipe.</summary>
    public string? Destino { get; set; }
    public int? ClienteId { get; set; }
    public int? AgendamentoId { get; set; }
    /// <summary>JSON com o que o texto precisa (nomes, horário no fuso da empresa).</summary>
    public string Dados { get; set; } = "{}";
    public string Status { get; set; } = "pendente";
    public int Tentativas { get; set; }
    public DateTime ProximaTentativa { get; set; }
    public string? UltimoErro { get; set; }
    /// <summary>Id da mensagem no provedor, para casar com os webhooks de entrega.</summary>
    public string? ProvedorId { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime? DataEnvio { get; set; }

    public Agendamento? Agendamento { get; set; }
}
