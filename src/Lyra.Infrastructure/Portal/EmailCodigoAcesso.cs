using System.Net.Http.Json;
using System.Text.Json;
using Lyra.Infrastructure.Notificacoes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Portal;

/// <summary>
/// Manda o código de acesso na hora, direto pela AW.Mensageria (sem a fila de notificações).
/// É transacional: chega mesmo a quem pediu para não receber os avisos da empresa.
/// </summary>
public sealed class EmailCodigoAcesso(
    HttpClient http,
    IOptions<EmailOptions> opcoes,
    LeitorEmpresa empresas,
    IHostEnvironment ambiente,
    ILogger<EmailCodigoAcesso> logger)
{
    public const string PrefixoChave = "lyra-acesso-";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task EnviarAsync(long id, string email, string codigo, int empresaId, int validadeMinutos, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opcoes.Value.TokenInterno))
        {
            if (ambiente.IsDevelopment())
            {
                logger.LogWarning("Sem Mensageria configurada: o código de acesso de {Email} é {Codigo}", email, codigo);
                return;
            }
            throw new InvalidOperationException("O envio de e-mails não está disponível agora. Tente mais tarde.");
        }

        var empresa = await empresas.ObterAsync(empresaId, ct);
        var montado = ModelosEmail.MontarCodigo(codigo, empresa, validadeMinutos);
        var corpo = new
        {
            canal = "email",
            chave = $"{PrefixoChave}{id}",
            destino = email,
            transacional = true,
            email = new
            {
                assunto = montado.Assunto,
                html = montado.Html,
                texto = montado.Texto,
                remetenteNome = empresa?.Nome ?? "Lyra",
                tags = new Dictionary<string, string> { ["evento"] = "codigo_acesso" },
            },
        };

        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{opcoes.Value.UrlMensageria.TrimEnd('/')}/api/mensageria/envios")
        {
            Content = JsonContent.Create(corpo, options: Json),
        };
        pedido.Headers.Add("X-Internal-Service-Token", opcoes.Value.TokenInterno);
        pedido.Headers.Add("X-Empresa-Id", empresaId.ToString());
        pedido.Headers.Add("X-Origin-System", CanalEmail.SiglaSistema);

        using var resposta = await http.SendAsync(pedido, ct);
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        if (!resposta.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Mensageria {(int)resposta.StatusCode} ao mandar o código de acesso: {(texto.Length > 300 ? texto[..300] : texto)}");
        }
        using var doc = JsonDocument.Parse(texto);
        if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "descadastrado")
        {
            throw new InvalidOperationException("Não conseguimos mandar e-mails para este endereço (ele já voltou ou foi marcado como spam). Use outro e-mail.");
        }
    }
}
