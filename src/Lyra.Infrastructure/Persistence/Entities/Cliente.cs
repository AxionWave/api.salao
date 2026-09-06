namespace Lyra.Infrastructure.Persistence.Entities;

public sealed class Cliente
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public string Nome { get; set; } = string.Empty;
    /// <summary>Somente dígitos. Único por empresa.</summary>
    public string Telefone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Observacoes { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
}
