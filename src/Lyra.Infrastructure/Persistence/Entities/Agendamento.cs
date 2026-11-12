namespace Lyra.Infrastructure.Persistence.Entities;

public sealed class Agendamento
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int ClienteId { get; set; }
    public int ProfissionalId { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    /// <summary>Preparo depois do atendimento (cópia do serviço na hora de marcar). Ocupa a agenda até Fim + este valor.</summary>
    public int IntervaloAposMinutos { get; set; }
    public string Status { get; set; } = "agendado";
    public string? Observacoes { get; set; }
    /// <summary>Agendamento que este substituiu ao remarcar.</summary>
    public int? RemarcadoDeId { get; set; }
    public string? MotivoCancelamento { get; set; }
    /// <summary>
    /// Conta do portal que marcou (a remarcação herda). A área do cliente mostra estes e os das fichas com o
    /// e-mail da conta; a ficha achada só pelo telefone não entra, porque o telefone não é verificado.
    /// </summary>
    public int? ContaClienteId { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }

    public Cliente Cliente { get; set; } = null!;
    public Profissional Profissional { get; set; } = null!;
    public ICollection<AgendamentoItem> Itens { get; set; } = [];
}
