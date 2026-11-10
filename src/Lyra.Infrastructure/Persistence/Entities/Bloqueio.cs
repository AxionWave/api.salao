namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>Período em que não se agenda: férias, feriado, folga, reunião.</summary>
public sealed class Bloqueio
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    /// <summary>Nulo = vale para a empresa toda.</summary>
    public int? ProfissionalId { get; set; }
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public string? Motivo { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;

    public Profissional? Profissional { get; set; }
}
