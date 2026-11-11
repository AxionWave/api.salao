namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>Até quando cada usuário já viu os avisos do sino, por empresa.</summary>
public sealed class PainelLeitura
{
    public int EmpresaId { get; set; }
    public int UsuarioId { get; set; }
    public DateTime VistoAte { get; set; }
}
