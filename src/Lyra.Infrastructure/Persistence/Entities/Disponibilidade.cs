namespace Lyra.Infrastructure.Persistence.Entities;

public sealed class Disponibilidade
{
    public int Id { get; set; }
    public int ProfissionalId { get; set; }
    /// <summary>0 = domingo … 6 = sábado (DateTime.DayOfWeek).</summary>
    public int DiaSemana { get; set; }
    public TimeOnly Inicio { get; set; }
    public TimeOnly Fim { get; set; }
    public Profissional Profissional { get; set; } = null!;
}
