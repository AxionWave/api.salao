namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>Código de 6 dígitos mandado por e-mail para entrar no portal. Só o hash fica guardado.</summary>
public sealed class CodigoAcesso
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    /// <summary>Empresa da página onde foi pedido: o e-mail sai com o nome dela.</summary>
    public int EmpresaId { get; set; }
    public string CodigoHash { get; set; } = string.Empty;
    public DateTime Expira { get; set; }
    public int Tentativas { get; set; }
    public DateTime? UsadoEm { get; set; }
    public string? Ip { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
}
