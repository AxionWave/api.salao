namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>
/// Quem agenda pelo portal. Uma por e-mail, valendo para todas as empresas; em cada empresa
/// a conta se liga à ficha de cliente pelo e-mail (ou telefone) na hora de agendar.
/// </summary>
public sealed class ContaCliente
{
    public int Id { get; set; }
    /// <summary>Minúsculas. Confirmado pelo código de acesso.</summary>
    public string Email { get; set; } = string.Empty;
    public string? Nome { get; set; }
    /// <summary>Somente dígitos.</summary>
    public string? Telefone { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoAcesso { get; set; }
}
