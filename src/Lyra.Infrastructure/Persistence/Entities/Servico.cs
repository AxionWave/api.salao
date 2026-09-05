namespace Lyra.Infrastructure.Persistence.Entities;

public sealed class Servico
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int DuracaoMinutos { get; set; }
    public decimal Preco { get; set; }
    public string? Categoria { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime? DataAtualizacao { get; set; }
}
