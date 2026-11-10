using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>
/// Link sem login que o cliente recebe para ver e confirmar o agendamento: o id assinado com HMAC.
/// Não expira sozinho; vale enquanto o agendamento puder ser confirmado.
/// </summary>
public sealed class LinksPublicos
{
    private const int TamanhoAssinatura = 12;
    private readonly byte[] chave;
    private readonly string urlBase;

    public LinksPublicos(IConfiguration configuration, IOptions<EmailOptions> email)
    {
        var segredo = new[]
            {
                configuration["Notificacoes:ChaveLinks"],
                Environment.GetEnvironmentVariable("JWT_SECRET"),
                configuration["Jwt:Secret"],
            }
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        // Derivada, para que vazar um link não ajude a forjar tokens de login.
        chave = segredo is null
            ? RandomNumberGenerator.GetBytes(32)
            : HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), "lyra-links-publicos-v1"u8);
        urlBase = email.Value.UrlPublica.TrimEnd('/');
    }

    public string Token(int agendamentoId)
    {
        Span<byte> dados = stackalloc byte[4 + TamanhoAssinatura];
        BinaryPrimitives.WriteInt32BigEndian(dados, agendamentoId);
        HMACSHA256.HashData(chave, dados[..4])[..TamanhoAssinatura].CopyTo(dados[4..]);
        return Convert.ToBase64String(dados).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string Url(int agendamentoId) => $"{urlBase}/c/{Token(agendamentoId)}";

    public int? Validar(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 40) return null;
        byte[] dados;
        try
        {
            var b64 = token.Replace('-', '+').Replace('_', '/');
            dados = Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
        }
        catch (FormatException)
        {
            return null;
        }
        if (dados.Length != 4 + TamanhoAssinatura) return null;
        var esperado = HMACSHA256.HashData(chave, dados.AsSpan(0, 4))[..TamanhoAssinatura];
        if (!CryptographicOperations.FixedTimeEquals(esperado, dados.AsSpan(4))) return null;
        var id = BinaryPrimitives.ReadInt32BigEndian(dados);
        return id > 0 ? id : null;
    }
}
