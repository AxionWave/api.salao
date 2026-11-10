namespace Lyra.Infrastructure.Persistence.Entities;

public sealed class Cliente
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public string Nome { get; set; } = string.Empty;
    /// <summary>Somente dígitos. Único por empresa, exceto entre membros da mesma família.</summary>
    public string Telefone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Observacoes { get; set; }
    public bool Ativo { get; set; } = true;
    public int? FamiliaId { get; set; }
    public Familia? Familia { get; set; }
    /// <summary>Texto livre: "mãe", "filho", "responsável".</summary>
    public string? Parentesco { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
}
