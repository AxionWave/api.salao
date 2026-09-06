namespace Lyra.Infrastructure.Persistence.Entities;

public sealed class AgendamentoItem
{
    public int Id { get; set; }
    public int AgendamentoId { get; set; }
    public int ServicoId { get; set; }
    public int Ordem { get; set; }
    public int DuracaoMinutos { get; set; }
    public decimal Preco { get; set; }

    public Agendamento Agendamento { get; set; } = null!;
    public Servico Servico { get; set; } = null!;
}
