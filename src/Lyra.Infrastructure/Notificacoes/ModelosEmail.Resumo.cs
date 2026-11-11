using System.Text;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>A agenda do dia de um profissional, congelada na hora em que o resumo foi programado.</summary>
public sealed class DadosResumo
{
    public string Profissional { get; init; } = "";
    /// <summary><c>yyyy-MM-dd</c> no fuso da empresa: abre a Agenda no dia.</summary>
    public string Dia { get; init; } = "";
    /// <summary>"quarta-feira, 18/11".</summary>
    public string DiaLocal { get; init; } = "";
    /// <summary>Termo da empresa para agendamento (horário, sessão, consulta), em minúsculas.</summary>
    public string Singular { get; init; } = "horário";
    public string Plural { get; init; } = "horários";
    public List<ItemResumo> Itens { get; init; } = [];
}

public sealed class ItemResumo
{
    public string Inicio { get; init; } = "";
    public string Fim { get; init; } = "";
    public string Cliente { get; init; } = "";
    public List<string> Servicos { get; init; } = [];
    public bool Confirmado { get; init; }
    public string? Observacoes { get; init; }
}

public static partial class ModelosEmail
{
    /// <summary>E-mail da manhã para o profissional, com a agenda do dia. Usa a terminologia da empresa (é para a equipe).</summary>
    public static EmailMontado MontarResumo(DadosResumo d, EmpresaRemetente? empresa, string? linkAgenda)
    {
        var nomeEmpresa = empresa?.Nome ?? "Lyra";
        var primeiroNome = d.Profissional.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? d.Profissional;
        var total = d.Itens.Count;
        var confirmados = d.Itens.Count(i => i.Confirmado);
        var quantos = $"{total} {(total == 1 ? d.Singular : d.Plural)}";
        var assunto = $"Sua agenda de hoje: {quantos} – {nomeEmpresa}";
        var titulo = $"Bom dia, {primeiroNome}!";
        var abertura = $"Hoje, {d.DiaLocal}, você tem {quantos}"
            + (confirmados == 0 ? "." : confirmados == total ? ", todos confirmados." : $", {confirmados} já confirmado{(confirmados == 1 ? "" : "s")}.");
        var rodape = $"Resumo automático enviado por {nomeEmpresa} pelo Lyra. Para não receber mais, peça a quem administra o Lyra para desligar o resumo do dia em Configurações.";

        var texto = new StringBuilder().AppendLine(titulo).AppendLine().AppendLine(abertura).AppendLine();
        foreach (var i in d.Itens)
        {
            texto.Append($"{i.Inicio}–{i.Fim}  {i.Cliente}");
            if (i.Servicos.Count > 0) texto.Append($" · {string.Join(", ", i.Servicos)}");
            texto.AppendLine(i.Confirmado ? " (confirmado)" : "");
            if (i.Observacoes is { } obs) texto.AppendLine($"    Obs.: {obs}");
        }
        texto.AppendLine();
        if (linkAgenda is not null) texto.AppendLine($"Abrir a agenda: {linkAgenda}").AppendLine();
        texto.AppendLine(rodape);

        var linhas = new StringBuilder();
        foreach (var i in d.Itens)
        {
            var selo = i.Confirmado
                ? """<span style="display:inline-block;margin-top:4px;background:#f0fdf4;color:#15803d;font-size:11px;font-weight:700;padding:2px 8px;border-radius:999px">&#10003; Confirmado</span>"""
                : $"""<span style="display:inline-block;margin-top:4px;background:#f5f0eb;color:{Suave};font-size:11px;font-weight:600;padding:2px 8px;border-radius:999px">A confirmar</span>""";
            var servicos = i.Servicos.Count > 0
                ? $"""<div style="margin-top:2px;font-size:13px;color:#5c514a">{E(string.Join(", ", i.Servicos))}</div>"""
                : "";
            var obs = i.Observacoes is { } o
                ? $"""<div style="margin-top:4px;font-size:12px;color:{Suave}">&#128172; {E(o)}</div>"""
                : "";
            linhas.Append($"""
                <tr>
                <td width="64" valign="top" style="width:64px;padding:12px 12px 12px 0;border-top:1px solid {Linha}">
                <div style="font-size:16px;font-weight:700;color:{Tinta}">{E(i.Inicio)}</div>
                <div style="font-size:12px;color:{Suave}">{E(i.Fim)}</div>
                </td>
                <td valign="top" style="padding:12px 0;border-top:1px solid {Linha}">
                <div style="font-size:15px;font-weight:600;color:{Tinta}">{E(i.Cliente)}</div>
                {servicos}{selo}{obs}
                </td>
                </tr>
                """);
        }

        var acao = linkAgenda is null ? "" : $"""
            <tr><td align="center" style="padding:20px 32px 4px">
            <a href="{E(linkAgenda)}" style="display:inline-block;background:{Marca};color:#ffffff;text-decoration:none;font-size:16px;font-weight:600;padding:14px 36px;border-radius:10px">Abrir a agenda</a>
            </td></tr>
            """;

        var html = $"""
            <!doctype html>
            <html lang="pt-BR">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"><title>{E(assunto)}</title></head>
            <body style="margin:0;padding:0;background:#f5f0eb;{Fonte}">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0">{E(abertura)}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f5f0eb">
            <tr><td align="center" style="padding:32px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px">
            <tr><td align="center" style="padding:0 0 20px">
            {Cabecalho(empresa, nomeEmpresa)}
            </td></tr>
            <tr><td style="background:#ffffff;border-radius:16px;box-shadow:0 1px 3px rgba(31,26,23,.08)">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
            <tr><td height="6" style="height:6px;line-height:6px;font-size:0;background:{Marca};border-radius:16px 16px 0 0">&nbsp;</td></tr>
            <tr><td style="padding:28px 32px 0">
            <span style="display:inline-block;background:#fbf3ec;color:{Marca};font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;padding:6px 14px;border-radius:999px">&#9728;&nbsp; Agenda do dia</span>
            <h1 style="margin:16px 0 0;font-size:24px;line-height:1.25;color:{Tinta}">{E(titulo)}</h1>
            <p style="margin:8px 0 0;font-size:15px;line-height:1.6;color:#3d332c">{E(abertura)}</p>
            </td></tr>
            <tr><td style="padding:16px 32px 0">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">{linhas}</table>
            </td></tr>
            {acao}
            <tr><td style="height:28px;line-height:28px;font-size:0">&nbsp;</td></tr>
            </table>
            </td></tr>
            <tr><td align="center" style="padding:20px 24px 0;font-size:12px;line-height:1.5;color:{Suave}">
            <div style="font-weight:600;color:#5c514a">{E(nomeEmpresa)}</div>
            <div style="margin-top:12px;color:#a59a91">{E(rodape)}</div>
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body></html>
            """;
        return new EmailMontado(assunto, html, texto.ToString(), null);
    }
}
