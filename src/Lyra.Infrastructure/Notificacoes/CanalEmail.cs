using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>
/// E-mail ao cliente, com o convite de calendário anexo. O Lyra monta a mensagem; a AW.Mensageria entrega
/// (remetente do Lyra, novas tentativas no provedor e lista de descadastros).
/// </summary>
public sealed class CanalEmail(
    HttpClient http,
    IOptions<EmailOptions> opcoes,
    LeitorEmpresa empresas,
    LinksPublicos links) : ICanalNotificacao
{
    public const string NomeCanal = "email";
    public const string SiglaSistema = "LYR";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Nome => NomeCanal;

    public bool Aceita(string evento, string? origem, EmpresaConfig? config) =>
        config?.EnviarEmails == true
        // Quem confirmou pelo link já viu a confirmação na tela.
        && !(evento == EventosNotificacao.AgendamentoConfirmado && origem == FilaNotificacoes.OrigemCliente);

    public string? Destino(Cliente cliente)
    {
        var email = cliente.Email?.Trim();
        return string.IsNullOrEmpty(email) || !email.Contains('@') ? null : email;
    }

    public async Task<string?> EnviarAsync(Notificacao n, CancellationToken ct)
    {
        if (n.Destino is null) throw new NotificacaoRecusadaException("Sem e-mail de destino.");
        var dados = FilaNotificacoes.LerDados(n.Dados);
        var empresa = await empresas.ObterAsync(n.EmpresaId, ct);
        var link = n.AgendamentoId is int id ? links.Url(id) : null;
        var email = ModelosEmail.Montar(n.Evento, n.AgendamentoId ?? 0, dados, empresa, link);

        var corpo = new
        {
            canal = "email",
            // A mesma chave em toda nova tentativa: a Mensageria não manda duas vezes.
            chave = $"lyra-notificacao-{n.Id}",
            destino = n.Destino,
            referencia = n.AgendamentoId is int ag ? $"agendamento:{ag}" : null,
            email = new
            {
                assunto = email.Assunto,
                html = email.Html,
                texto = email.Texto,
                remetenteNome = empresa?.Nome ?? "Lyra",
                // E-mail da empresa mal cadastrado não pode impedir o envio ao cliente.
                responderPara = EmailValido(empresa?.Email) ? empresa!.Email!.Trim() : null,
                tags = new Dictionary<string, string> { ["evento"] = n.Evento },
                anexos = email.Ics is { } ics
                    ? new[]
                    {
                        new
                        {
                            nome = "agendamento.ics",
                            tipo = "text/calendar; charset=utf-8",
                            conteudoBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ics)),
                        },
                    }
                    : null,
            },
        };

        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{opcoes.Value.UrlMensageria.TrimEnd('/')}/api/mensageria/envios")
        {
            Content = JsonContent.Create(corpo, options: Json),
        };
        pedido.Headers.Add("X-Internal-Service-Token", opcoes.Value.TokenInterno);
        pedido.Headers.Add("X-Empresa-Id", n.EmpresaId.ToString());
        pedido.Headers.Add("X-Origin-System", SiglaSistema);

        using var resposta = await http.SendAsync(pedido, ct);
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        if (resposta.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(texto);
            var envio = doc.RootElement;
            if (envio.TryGetProperty("status", out var status) && status.GetString() == "descadastrado")
                throw new NotificacaoRecusadaException("O cliente pediu para não receber e-mails.");
            return envio.TryGetProperty("id", out var idEnvio) ? idEnvio.GetString() : null;
        }

        var erro = $"Mensageria {(int)resposta.StatusCode}: {Mensagem(texto)}";
        // 4xx (fora 408/429) é problema do pedido: tentar de novo dá no mesmo.
        if ((int)resposta.StatusCode is >= 400 and < 500
            && resposta.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))
        {
            throw new NotificacaoRecusadaException(erro);
        }
        throw new HttpRequestException(erro);
    }

    private static bool EmailValido(string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && !valor.Trim().Contains(' ')
        && MailAddress.TryCreate(valor.Trim(), out var endereco) && endereco.Address == valor.Trim();

    private static string Mensagem(string texto)
    {
        try
        {
            using var doc = JsonDocument.Parse(texto);
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString() ?? texto;
        }
        catch (JsonException)
        {
        }
        return texto.Length > 300 ? texto[..300] : texto;
    }
}
