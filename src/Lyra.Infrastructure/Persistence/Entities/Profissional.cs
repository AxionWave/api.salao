namespace Lyra.Infrastructure.Persistence.Entities;

public sealed class Profissional
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    /// <summary>Id lógico em core.pessoas. Sem FK cross-schema.</summary>
    public int? PessoaId { get; set; }
    public string Nome { get; set; } = string.Empty;
    /// <summary>Token de cor da agenda (copper, ember, ok, warn, destructive).</summary>
    public string Cor { get; set; } = "copper";
    public bool Ativo { get; set; } = true;
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
    public ICollection<Disponibilidade> Disponibilidades { get; set; } = [];
}
