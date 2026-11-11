namespace Lyra.Infrastructure.Notificacoes.Push;

internal static class Base64Url
{
    public static string Codificar(ReadOnlySpan<byte> dados) =>
        Convert.ToBase64String(dados).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Aceita base64url com ou sem "=" (e base64 comum). Inválido = FormatException.</summary>
    public static byte[] Decodificar(string texto)
    {
        var s = texto.Trim().Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
