using Lyra.Application.Abstractions;
using Lyra.Application.Push;
using Lyra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Notificacoes.Push;

public sealed class PushService(LyraDbContext db, IOptions<WebPushOptions> opcoes) : IPushService
{
    /// <summary>
    /// A API faz POST no endpoint que o navegador informou: só os serviços de push dos navegadores,
    /// para ninguém usar o Lyra para chamar endereços internos.
    /// </summary>
    private static readonly string[] ServicosConhecidos =
    [
        ".googleapis.com",          // Chrome, Edge no Android, Opera, Samsung
        ".push.services.mozilla.com",
        ".push.apple.com",          // Safari e PWA no iPhone
        ".notify.windows.com",      // Edge no Windows
    ];

    public PushChaveResponse Chave() =>
        new(opcoes.Value.Configurado ? opcoes.Value.PublicKey.Trim() : null);

    public async Task AssinarAsync(int empresaId, int usuarioId, AssinarPushRequest request, CancellationToken ct)
    {
        if (!opcoes.Value.Configurado) throw new InvalidOperationException("Avisos no aparelho não estão disponíveis nesta instalação.");
        var endpoint = Endpoint(request.Endpoint);
        var p256dh = Chave(request.P256dh, 65, "p256dh");
        var auth = Chave(request.Auth, 16, "auth");
        var aparelho = string.IsNullOrWhiteSpace(request.Aparelho) ? null : request.Aparelho.Trim()[..Math.Min(300, request.Aparelho.Trim().Length)];
        var agora = DateTime.UtcNow;

        // O mesmo navegador pode trocar de usuário (outra pessoa entrou): a assinatura passa a ser de quem assinou por último.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO lyra.push_assinaturas (empresa_id, usuario_id, endpoint, p256dh, auth, aparelho, data_criacao)
            VALUES ({empresaId}, {usuarioId}, {endpoint}, {p256dh}, {auth}, {aparelho}, {agora})
            ON CONFLICT (endpoint) DO UPDATE SET
                empresa_id = EXCLUDED.empresa_id, usuario_id = EXCLUDED.usuario_id,
                p256dh = EXCLUDED.p256dh, auth = EXCLUDED.auth, aparelho = EXCLUDED.aparelho
            """, ct);
    }

    public Task RemoverAsync(int usuarioId, string endpoint, CancellationToken ct) =>
        db.PushAssinaturas.Where(a => a.Endpoint == endpoint && a.UsuarioId == usuarioId).ExecuteDeleteAsync(ct);

    private static string Endpoint(string? valor)
    {
        var texto = (valor ?? "").Trim();
        if (texto.Length is 0 or > 1000
            || !Uri.TryCreate(texto, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !ServicosConhecidos.Any(s => ("." + uri.Host).EndsWith(s, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Endereço de push não reconhecido.");
        }
        return texto;
    }

    private static string Chave(string? valor, int tamanho, string nome)
    {
        try
        {
            var bytes = Base64Url.Decodificar(valor ?? "");
            if (bytes.Length == tamanho && (tamanho != 65 || bytes[0] == 0x04)) return Base64Url.Codificar(bytes);
        }
        catch (FormatException)
        {
        }
        throw new ArgumentException($"Chave {nome} inválida.");
    }
}
