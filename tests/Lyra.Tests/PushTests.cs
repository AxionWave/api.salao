using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lyra.Infrastructure.Persistence;
using Lyra.Tests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lyra.Tests.Infra.Cenario;

namespace Lyra.Tests;

[Collection(ColecaoApi.Nome)]
public sealed class PushTests(ApiFixture api)
{
    /// <summary>O lado do navegador: o par de chaves e o segredo que ele gera ao assinar.</summary>
    private sealed class Aparelho : IDisposable
    {
        public ECDiffieHellman Chave { get; } = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        public byte[] Auth { get; } = RandomNumberGenerator.GetBytes(16);
        public string Endpoint { get; } = $"https://fcm.googleapis.com/fcm/send/teste-{Guid.NewGuid():N}";

        public byte[] Publica
        {
            get
            {
                var p = Chave.ExportParameters(false);
                return [0x04, .. p.Q.X!, .. p.Q.Y!];
            }
        }

        public object Assinatura() => new { endpoint = Endpoint, p256dh = B64(Publica), auth = B64(Auth), aparelho = "Chrome no Windows" };

        /// <summary>Decifra como o navegador (RFC 8291 / RFC 8188, aes128gcm).</summary>
        public JsonElement Abrir(byte[] corpo)
        {
            var salt = corpo[..16];
            var rs = BinaryPrimitives.ReadUInt32BigEndian(corpo.AsSpan(16, 4));
            Assert.Equal(4096u, rs);
            var tamanho = corpo[20];
            var remetente = corpo[21..(21 + tamanho)];
            var cifrado = corpo[(21 + tamanho)..];

            using var outro = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = remetente[1..33], Y = remetente[33..65] },
            });
            var compartilhado = Chave.DeriveRawSecretAgreement(outro.PublicKey);
            byte[] info = [.. "WebPush: info\0"u8, .. Publica, .. remetente];
            var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, compartilhado, 32, Auth, info);
            var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
            var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

            var claro = new byte[cifrado.Length - 16];
            using var aes = new AesGcm(cek, 16);
            aes.Decrypt(nonce, cifrado[..^16], cifrado[^16..], claro);
            Assert.Equal(0x02, claro[^1]);
            return JsonDocument.Parse(claro[..^1]).RootElement.Clone();
        }

        public void Dispose() => Chave.Dispose();
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DeB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    private Task<int> Assinaturas(string endpoint) => NoBanco(db => db.PushAssinaturas.CountAsync(a => a.Endpoint == endpoint));

    [Fato]
    public async Task Entrega_a_chave_publica_e_so_aceita_servicos_de_push_conhecidos()
    {
        var c = await Criar(api);
        using var aparelho = new Aparelho();

        Assert.Equal(api.ChavePushPublica, Exigir(await c.Admin.Get("/api/salao/push/chave"), 200).Json.Str("chavePublica"));
        Exigir(await c.Admin.Post("/api/salao/push/assinaturas",
            new { endpoint = "https://intranet.lumen.local/push", p256dh = B64(aparelho.Publica), auth = B64(aparelho.Auth) }), 400);
        Exigir(await c.Admin.Post("/api/salao/push/assinaturas",
            new { endpoint = "http://fcm.googleapis.com/fcm/send/x", p256dh = B64(aparelho.Publica), auth = B64(aparelho.Auth) }), 400);
        Exigir(await c.Admin.Post("/api/salao/push/assinaturas",
            new { endpoint = aparelho.Endpoint, p256dh = "curta", auth = B64(aparelho.Auth) }), 400);
        Exigir(await c.Admin.Post("/api/salao/push/assinaturas", aparelho.Assinatura()), 204);
        Exigir(await c.Admin.Post("/api/salao/push/assinaturas", aparelho.Assinatura()), 204);

        Assert.Equal(1, await Assinaturas(aparelho.Endpoint));
    }

    [Fato]
    public async Task Agendamento_avisa_os_aparelhos_da_equipe_menos_os_de_quem_marcou()
    {
        var c = await Criar(api);
        var recepcao = new Api(c.Http, Tokens.Gerar(c.EmpresaId, Tokens.Recepcao, usuarioId: 2));
        using var meu = new Aparelho();
        using var dela = new Aparelho();
        Exigir(await c.Admin.Post("/api/salao/push/assinaturas", meu.Assinatura()), 204);
        Exigir(await recepcao.Post("/api/salao/push/assinaturas", dela.Assinatura()), 204);

        var id = await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");
        await api.ProcessarFila();

        Assert.Empty(api.Push.Para(meu.Endpoint));
        var pedido = Assert.Single(api.Push.Para(dela.Endpoint));
        Assert.Equal("aes128gcm", pedido.Cabecalhos["Content-Encoding"]);
        Assert.Equal("86400", pedido.Cabecalhos["TTL"]);
        Assert.Equal($"ag{id}", pedido.Cabecalhos["Topic"]);

        var aviso = dela.Abrir(pedido.Corpo);
        Assert.Equal("Novo horário", aviso.Str("titulo"));
        Assert.Equal($"Maria · Ana · {c.Terca.ToString("dd/MM", CultureInfo.InvariantCulture)} às 15:00", aviso.Str("corpo"));
        Assert.Equal($"agenda?data={Dia(c.Terca)}&visao=dia", aviso.Str("url"));
        Assert.Equal($"lyra-agendamento-{id}", aviso.Str("tag"));

        // VAPID: JWT ES256 para o serviço de push, assinado com a chave da API.
        var autorizacao = pedido.Cabecalhos["Authorization"];
        Assert.StartsWith("vapid t=", autorizacao);
        var partes = autorizacao["vapid t=".Length..].Split(", k=");
        Assert.Equal(api.ChavePushPublica, partes[1]);
        var jwt = partes[0].Split('.');
        var claims = JsonDocument.Parse(DeB64(jwt[1])).RootElement;
        Assert.Equal("https://fcm.googleapis.com", claims.Str("aud"));
        Assert.Equal("mailto:testes@lyra.teste", claims.Str("sub"));
        Assert.True(claims.GetProperty("exp").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var chave = DeB64(api.ChavePushPublica);
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = chave[1..33], Y = chave[33..65] },
        });
        Assert.True(ecdsa.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), DeB64(jwt[2]), HashAlgorithmName.SHA256));
    }

    [Fato]
    public async Task Assinatura_vencida_sai_sozinha_e_cada_um_remove_so_a_sua()
    {
        var c = await Criar(api);
        var recepcao = new Api(c.Http, Tokens.Gerar(c.EmpresaId, Tokens.Recepcao, usuarioId: 2));
        using var vencido = new Aparelho();
        using var outro = new Aparelho();
        Exigir(await recepcao.Post("/api/salao/push/assinaturas", vencido.Assinatura()), 204);
        Exigir(await recepcao.Post("/api/salao/push/assinaturas", outro.Assinatura()), 204);
        api.Push.RespostaPorEndpoint[vencido.Endpoint] = HttpStatusCode.Gone;

        await c.AgendarOk(c.Maria, c.Ana, [c.Consulta], c.Terca, "15:00");
        await api.ProcessarFila();

        Assert.Equal(0, await Assinaturas(vencido.Endpoint));
        Assert.Single(api.Push.Para(outro.Endpoint));

        Exigir(await c.Admin.Post("/api/salao/push/assinaturas/remover", new { endpoint = outro.Endpoint }), 204);
        Assert.Equal(1, await Assinaturas(outro.Endpoint));
        Exigir(await recepcao.Post("/api/salao/push/assinaturas/remover", new { endpoint = outro.Endpoint }), 204);
        Assert.Equal(0, await Assinaturas(outro.Endpoint));
    }

    private async Task<T> NoBanco<T>(Func<LyraDbContext, Task<T>> consulta)
    {
        using var scope = api.Servicos.CreateScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<LyraDbContext>());
    }
}
