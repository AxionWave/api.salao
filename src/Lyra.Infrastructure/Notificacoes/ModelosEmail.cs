using System.Globalization;
using System.Text;
using Lyra.Application.Notificacoes;

namespace Lyra.Infrastructure.Notificacoes;

public sealed record EmailMontado(string Assunto, string Html, string Texto, string? Ics);

/// <summary>Textos dos e-mails ao cliente. Usa "horário" em vez da terminologia da empresa, que vale para a equipe.</summary>
public static class ModelosEmail
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static EmailMontado Montar(
        string evento, int agendamentoId, DadosNotificacao d, EmpresaRemetente? empresa, string? linkConfirmar)
    {
        var nomeEmpresa = empresa?.Nome ?? "Lyra";
        var quando = Quando(d.InicioLocal);
        var primeiroNome = d.Cliente.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? d.Cliente;

        var (assunto, titulo, abertura, botao) = evento switch
        {
            EventosNotificacao.AgendamentoRemarcado => (
                $"Horário alterado: {quando.Curto} – {nomeEmpresa}",
                "Seu horário mudou",
                d.InicioAnteriorLocal is { } antes
                    ? $"Seu horário em {nomeEmpresa} passou de {Quando(antes).Longo} para {quando.Longo}."
                    : $"Seu horário em {nomeEmpresa} foi alterado para {quando.Longo}.",
                linkConfirmar),
            EventosNotificacao.AgendamentoCancelado => (
                $"Horário cancelado: {quando.Curto} – {nomeEmpresa}",
                "Seu horário foi cancelado",
                $"O horário de {quando.Longo} em {nomeEmpresa} foi cancelado.",
                null),
            EventosNotificacao.AgendamentoConfirmado => (
                $"Presença confirmada: {quando.Curto} – {nomeEmpresa}",
                "Presença confirmada",
                $"Está confirmado: esperamos você em {nomeEmpresa} {quando.Longo}.",
                null),
            _ => (
                $"Horário marcado: {quando.Curto} – {nomeEmpresa}",
                "Seu horário está marcado",
                $"Seu horário em {nomeEmpresa} está marcado para {quando.Longo}.",
                linkConfirmar),
        };

        var linhas = new List<(string Rotulo, string Valor)>();
        if (d.Servicos.Count > 0) linhas.Add(("Serviço", string.Join(", ", d.Servicos)));
        if (!string.IsNullOrWhiteSpace(d.Profissional)) linhas.Add(("Com", d.Profissional));
        linhas.Add(("Quando", Capitalizar(quando.Longo)));
        if (empresa?.Endereco is { } endereco) linhas.Add(("Onde", endereco));
        if (evento == EventosNotificacao.AgendamentoCancelado
            && d.Motivo is { } motivo && !motivo.Equals("Remarcado", StringComparison.OrdinalIgnoreCase))
        {
            linhas.Add(("Motivo", motivo));
        }

        var contato = Contato(empresa);
        var fecho = evento == EventosNotificacao.AgendamentoCancelado
            ? $"Para marcar outro horário, {contato}."
            : $"Se precisar mudar alguma coisa, {contato}.";
        var rodape = $"Mensagem automática enviada por {nomeEmpresa} pelo Lyra.";

        var texto = new StringBuilder()
            .AppendLine($"Olá, {primeiroNome}!").AppendLine()
            .AppendLine(abertura).AppendLine();
        foreach (var (rotulo, valor) in linhas) texto.AppendLine($"{rotulo}: {valor}");
        if (botao is not null) texto.AppendLine().AppendLine($"Confirme sua presença: {botao}");
        texto.AppendLine().AppendLine(fecho).AppendLine().AppendLine(rodape);

        var html = Html(titulo, $"Olá, {primeiroNome}!", abertura, linhas, botao, fecho, rodape);
        var uid = d.Cadeia > 0 ? d.Cadeia : agendamentoId;
        var ics = Ics(evento, uid, d, nomeEmpresa, empresa?.Endereco);
        return new EmailMontado(assunto, html, texto.ToString(), ics);
    }

    private static string Contato(EmpresaRemetente? e) => (e?.Email, e?.Telefone) switch
    {
        (not null, not null) => $"responda este e-mail ou ligue para {e!.Telefone}",
        (not null, null) => "é só responder este e-mail",
        (null, not null) => $"fale com a gente pelo {e!.Telefone}",
        _ => "fale com a gente",
    };

    private sealed record Momento(string Curto, string Longo);

    private static Momento Quando(string local)
    {
        if (!DateTime.TryParseExact(local, "dd/MM/yyyy HH:mm", PtBr, DateTimeStyles.None, out var dt))
        {
            return new Momento(local, local);
        }
        return new Momento(
            dt.ToString("dd/MM 'às' HH:mm", PtBr),
            dt.ToString("dddd, dd/MM 'às' HH:mm", PtBr));
    }

    private static string Capitalizar(string s) => s.Length == 0 ? s : char.ToUpper(s[0], PtBr) + s[1..];

    private static string Html(
        string titulo, string saudacao, string abertura, List<(string Rotulo, string Valor)> linhas,
        string? botao, string fecho, string rodape)
    {
        static string E(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("'", "&#39;");
        var tabela = new StringBuilder();
        foreach (var (rotulo, valor) in linhas)
        {
            tabela.Append($"""<tr><td style="padding:6px 12px 6px 0;color:#6b7280;vertical-align:top;white-space:nowrap">{E(rotulo)}</td><td style="padding:6px 0;color:#111827">{E(valor)}</td></tr>""");
        }
        var acao = botao is null ? "" :
            $"""<p style="margin:24px 0"><a href="{E(botao)}" style="background:#b8733a;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;display:inline-block;font-weight:600">Confirmar presença</a></p>""";
        return $"""
            <!doctype html>
            <html lang="pt-BR"><body style="margin:0;padding:24px;background:#f3f4f6;font-family:Arial,Helvetica,sans-serif">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px">
            <tr><td style="padding:28px">
            <h1 style="margin:0 0 16px;font-size:20px;color:#111827">{E(titulo)}</h1>
            <p style="margin:0 0 8px;color:#111827">{E(saudacao)}</p>
            <p style="margin:0 0 16px;color:#111827">{E(abertura)}</p>
            <table role="presentation" cellpadding="0" cellspacing="0" style="font-size:14px">{tabela}</table>
            {acao}
            <p style="margin:16px 0 0;color:#111827">{E(fecho)}</p>
            </td></tr></table>
            <p style="max-width:560px;margin:12px auto 0;font-size:12px;color:#6b7280;text-align:center">{E(rodape)}</p>
            </body></html>
            """;
    }

    /// <summary>Convite de calendário. Mesmo UID para toda a cadeia de remarcações, então o evento é atualizado em vez de duplicado.</summary>
    private static string Ics(string evento, int uid, DadosNotificacao d, string empresa, string? endereco)
    {
        var cancelado = evento == EventosNotificacao.AgendamentoCancelado;
        static string Utc(DateTime t) => DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
        var resumo = d.Servicos.Count > 0 ? $"{string.Join(", ", d.Servicos)} – {empresa}" : empresa;
        var fim = d.Fim > d.Inicio ? d.Fim : d.Inicio.AddMinutes(30);
        var sb = new StringBuilder()
            .Append("BEGIN:VCALENDAR\r\n")
            .Append("VERSION:2.0\r\n")
            .Append("PRODID:-//Lumen//Lyra//PT-BR\r\n")
            .Append("CALSCALE:GREGORIAN\r\n")
            .Append(cancelado ? "METHOD:CANCEL\r\n" : "METHOD:PUBLISH\r\n")
            .Append("BEGIN:VEVENT\r\n")
            .Append($"UID:lyra-agendamento-{uid}@lyra.lumensoft.com.br\r\n")
            .Append($"SEQUENCE:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60}\r\n")
            .Append($"DTSTAMP:{Utc(DateTime.UtcNow)}\r\n")
            .Append($"DTSTART:{Utc(d.Inicio)}\r\n")
            .Append($"DTEND:{Utc(fim)}\r\n")
            .Append($"SUMMARY:{Esc(resumo)}\r\n");
        if (!string.IsNullOrWhiteSpace(d.Profissional)) sb.Append($"DESCRIPTION:{Esc("Com " + d.Profissional)}\r\n");
        if (endereco is not null) sb.Append($"LOCATION:{Esc(endereco)}\r\n");
        sb.Append(cancelado ? "STATUS:CANCELLED\r\n" : "STATUS:CONFIRMED\r\n")
            .Append("END:VEVENT\r\n")
            .Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }
}
