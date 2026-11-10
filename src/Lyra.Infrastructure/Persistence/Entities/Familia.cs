namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>Grupo de clientes da mesma casa: podem dividir telefone e e-mail.</summary>
public sealed class Familia
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public List<Cliente> Membros { get; set; } = [];
}
