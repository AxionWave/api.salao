using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>E-mail ao cliente pelo Resend, com o convite de calendário anexo.</summary>
public sealed class CanalEmail(
    HttpClient http,
    IOptions<EmailOptions> opcoes,
    LeitorEmpresa empresas,
    LinksPublicos links) : ICanalNotificacao
{
    public const string NomeCanal = "email";

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

        var corpo = new Dictionary<string, object?>
        {
            ["from"] = $"{NomeExibido(empresa?.Nome ?? "Lyra")} <{opcoes.Value.Remetente}>",
            ["to"] = new[] { n.Destino },
            ["subject"] = email.Assunto,
            ["html"] = email.Html,
            ["text"] = email.Texto,
            ["tags"] = new[] { new { name = "evento", value = n.Evento } },
        };
        if (empresa?.Email is { } responder) corpo["reply_to"] = responder;
        if (email.Ics is { } ics)
        {
            corpo["attachments"] = new[]
            {
                new
                {
                    filename = "agendamento.ics",
                    content = Convert.ToBase64String(Encoding.UTF8.GetBytes(ics)),
                    content_type = "text/calendar; charset=utf-8",
                },
            };
        }

        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{opcoes.Value.UrlApi.TrimEnd('/')}/emails")
        {
            Content = JsonContent.Create(corpo, options: Json),
        };
        pedido.Headers.Authorization = new("Bearer", opcoes.Value.ApiKey);
        // Se a resposta se perder e o worker tentar de novo, o Resend não manda duas vezes.
        pedido.Headers.Add("Idempotency-Key", $"lyra-notificacao-{n.Id}");

        using var resposta = await http.SendAsync(pedido, ct);
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        if (resposta.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(texto);
            return doc.RootElement.TryGetProperty("id", out var idResend) ? idResend.GetString() : null;
        }

        var erro = $"Resend {(int)resposta.StatusCode}: {Mensagem(texto)}";
        // 4xx (fora 408/409/429) é problema da mensagem: tentar de novo dá no mesmo.
        if ((int)resposta.StatusCode is >= 400 and < 500
            && resposta.StatusCode is not (HttpStatusCode.RequestTimeout or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests))
        {
            throw new NotificacaoRecusadaException(erro);
        }
        throw new HttpRequestException(erro);
    }

    private static string NomeExibido(string nome)
    {
        var limpo = new string(nome.Where(c => c is not ('<' or '>' or '"' or '\r' or '\n')).ToArray()).Trim();
        return $"\"{(limpo.Length > 60 ? limpo[..60] : limpo)}\"";
    }

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
