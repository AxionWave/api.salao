namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>Uma linha por empresa. Ausente = segmento padrão.</summary>
public sealed class EmpresaConfig
{
    public int EmpresaId { get; set; }
    public string Segmento { get; set; } = "beleza";
    /// <summary>Id IANA (ex.: America/Sao_Paulo).</summary>
    public string FusoHorario { get; set; } = "America/Sao_Paulo";
    /// <summary>JSON da terminologia personalizada. Nulo = usa a do segmento.</summary>
    public string? Terminologia { get; set; }
    /// <summary>Clientes com e-mail recebem os avisos de agendamento. Começa desligado.</summary>
    public bool EnviarEmails { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
}
