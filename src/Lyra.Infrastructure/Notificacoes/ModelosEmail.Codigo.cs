namespace Lyra.Infrastructure.Notificacoes;

public static partial class ModelosEmail
{
    /// <summary>Código de 6 dígitos para entrar na página da empresa e agendar.</summary>
    public static EmailMontado MontarCodigo(string codigo, EmpresaRemetente? empresa, int validadeMinutos)
    {
        var nomeEmpresa = empresa?.Nome ?? "Lyra";
        var assunto = $"{codigo} é o seu código de acesso – {nomeEmpresa}";
        var abertura = $"Use este código para entrar e agendar em {nomeEmpresa}. Ele vale por {validadeMinutos} minutos.";
        var aviso = "Se não foi você que pediu, pode ignorar este e-mail: sem o código ninguém entra.";
        var texto = $"""
            Seu código de acesso: {codigo}

            {abertura}

            {aviso}

            Enviado por {nomeEmpresa} pelo Lyra.
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
            <tr><td align="center" style="padding:28px 32px 0">
            <h1 style="margin:0;font-size:22px;line-height:1.3;color:{Tinta}">Seu código de acesso</h1>
            <p style="margin:8px 0 0;font-size:15px;line-height:1.6;color:#3d332c">{E(abertura)}</p>
            </td></tr>
            <tr><td align="center" style="padding:24px 32px 8px">
            <div style="display:inline-block;padding:14px 24px;border:1px solid {Linha};border-radius:14px;background:#fbf8f5;font-size:34px;font-weight:700;letter-spacing:.35em;color:{Tinta};font-family:'Segoe UI Mono',Consolas,Menlo,monospace">{E(codigo)}</div>
            </td></tr>
            <tr><td align="center" style="padding:16px 32px 32px;font-size:13px;line-height:1.6;color:{Suave}">{E(aviso)}</td></tr>
            </table>
            </td></tr>
            <tr><td align="center" style="padding:20px 24px 0;font-size:12px;line-height:1.5;color:{Suave}">
            <div style="font-weight:600;color:#5c514a">{E(nomeEmpresa)}</div>
            <div style="margin-top:12px;color:#a59a91">Enviado por {E(nomeEmpresa)} pelo Lyra.</div>
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body></html>
            """;
        return new EmailMontado(assunto, html, texto, null);
    }
}
