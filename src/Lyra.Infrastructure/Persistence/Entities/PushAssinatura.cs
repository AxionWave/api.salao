namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>
/// Um navegador ou celular que aceitou receber avisos (Web Push). O endpoint é do serviço de push do
/// navegador (Google, Mozilla, Apple) e identifica o aparelho; p256dh e auth cifram a mensagem para ele.
/// </summary>
public sealed class PushAssinatura
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public int UsuarioId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? Aparelho { get; set; }
    public DateTime DataCriacao { get; set; }
    public DateTime? UltimoEnvio { get; set; }
}
