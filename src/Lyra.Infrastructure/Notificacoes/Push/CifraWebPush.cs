using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Lyra.Infrastructure.Notificacoes.Push;

/// <summary>
/// Cifra a mensagem para um aparelho como pede o Web Push (RFC 8291, codificação aes128gcm do RFC 8188):
/// ECDH entre uma chave descartável e a do navegador, HKDF com o segredo "auth" e AES-128-GCM num registro só.
/// </summary>
public static class CifraWebPush
{
    /// <summary>Tamanho do registro declarado no cabeçalho; a mensagem cifrada cabe num só.</summary>
    private const int TamanhoRegistro = 4096;
    public const int MaximoMensagem = 3000;

    public static byte[] Cifrar(byte[] mensagem, string p256dh, string auth)
    {
        using var efemera = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Cifrar(mensagem, Base64Url.Decodificar(p256dh), Base64Url.Decodificar(auth), RandomNumberGenerator.GetBytes(16), efemera);
    }

    internal static byte[] Cifrar(byte[] mensagem, byte[] chaveAparelho, byte[] segredoAuth, byte[] salt, ECDiffieHellman efemera)
    {
        if (mensagem.Length > MaximoMensagem) throw new ArgumentException("Mensagem grande demais para o Web Push.");
        var publicaEfemera = Ponto(efemera);

        using var aparelho = ECDiffieHellman.Create(ParametrosPublicos(chaveAparelho));
        var compartilhado = efemera.DeriveRawSecretAgreement(aparelho.PublicKey);

        var infoChave = Juntar("WebPush: info\0"u8, chaveAparelho, publicaEfemera);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, compartilhado, 32, segredoAuth, infoChave);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

        // Último (e único) registro: os dados seguidos do delimitador 0x02, sem preenchimento.
        var claro = new byte[mensagem.Length + 1];
        mensagem.CopyTo(claro, 0);
        claro[^1] = 0x02;

        var saida = new byte[16 + 4 + 1 + publicaEfemera.Length + claro.Length + 16];
        salt.CopyTo(saida, 0);
        BinaryPrimitives.WriteUInt32BigEndian(saida.AsSpan(16, 4), TamanhoRegistro);
        saida[20] = (byte)publicaEfemera.Length;
        publicaEfemera.CopyTo(saida, 21);
        var inicio = 21 + publicaEfemera.Length;
        using var aes = new AesGcm(cek, 16);
        aes.Encrypt(nonce, claro, saida.AsSpan(inicio, claro.Length), saida.AsSpan(inicio + claro.Length, 16));
        return saida;
    }

    /// <summary>Ponto P-256 não comprimido (0x04 ‖ X ‖ Y), o formato de p256dh e da chave VAPID pública.</summary>
    public static ECParameters ParametrosPublicos(byte[] ponto)
    {
        if (ponto.Length != 65 || ponto[0] != 0x04) throw new ArgumentException("Chave P-256 inválida.");
        return new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = ponto[1..33], Y = ponto[33..65] },
        };
    }

    public static byte[] Ponto(AsymmetricAlgorithm chave)
    {
        var p = chave switch
        {
            ECDiffieHellman dh => dh.ExportParameters(false),
            ECDsa dsa => dsa.ExportParameters(false),
            _ => throw new ArgumentException("Chave sem curva elíptica."),
        };
        return [0x04, .. p.Q.X!, .. p.Q.Y!];
    }

    private static byte[] Juntar(ReadOnlySpan<byte> a, byte[] b, byte[] c) => [.. a, .. b, .. c];
}
