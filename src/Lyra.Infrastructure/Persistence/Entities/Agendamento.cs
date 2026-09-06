namespace Lyra.Infrastructure.Persistence.Entities;

public sealed class Agendamento
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public int ClienteId { get; set; }
    public int ProfissionalId { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public string Status { get; set; } = "agendado";
    public string? Observacoes { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }

    public Cliente Cliente { get; set; } = null!;
    public Profissional Profissional { get; set; } = null!;
    public ICollection<AgendamentoItem> Itens { get; set; } = [];
}
