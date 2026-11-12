using System.Globalization;
using System.Text;
using Lyra.Application.Configuracao;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Services;

namespace Lyra.Infrastructure.Notificacoes;

public sealed record EmailMontado(string Assunto, string Html, string Texto, string? Ics);

/// <summary>Textos da empresa para este evento (<see cref="ModelosAviso"/>); nulo = o padrão.</summary>
public sealed record TextosAviso(string? Assunto, string? Mensagem, string? Recado);

/// <summary>
/// Textos dos e-mails ao cliente. Usa "horário" em vez da terminologia da empresa, que vale para a equipe;
/// a empresa troca o assunto e a abertura em Configurações, se quiser outro nome.
/// </summary>
public static partial class ModelosEmail
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>A Mensageria troca pelo link de descadastro do envio.</summary>
    public const string MarcadorDescadastro = "{{descadastro}}";

    public static EmailMontado Montar(
        string evento, int agendamentoId, DadosNotificacao d, EmpresaRemetente? empresa, string? linkConfirmar,
        TextosAviso? textos = null)
    {
        var nomeEmpresa = empresa?.Nome ?? "Lyra";
        var cancelado = evento == EventosNotificacao.AgendamentoCancelado;
        var minutos = d.Fim > d.Inicio ? (int)Math.Round((d.Fim - d.Inicio).TotalMinutes) : 0;
        var quando = Quando(d.InicioLocal, minutos);
        var antes = d.InicioAnteriorLocal is { } anterior ? Quando(anterior, 0) : null;
        var primeiroNome = d.Cliente.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? d.Cliente;

        var (assunto, titulo, abertura, botao) = evento switch
        {
            EventosNotificacao.AgendamentoRemarcado => (
                $"Horário alterado: {quando.Curto} – {nomeEmpresa}",
                "Seu horário mudou",
                antes is not null
                    ? $"Seu horário em {nomeEmpresa} passou de {antes.Longo} para {quando.Longo}."
                    : $"Seu horário em {nomeEmpresa} foi alterado para {quando.Longo}.",
                d.Confirmado ? null : linkConfirmar),
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
            EventosNotificacao.AgendamentoLembrete => Lembrete(d, nomeEmpresa, quando, linkConfirmar),
            _ => (
                $"Horário marcado: {quando.Curto} – {nomeEmpresa}",
                "Seu horário está marcado",
                d.Confirmado
                    ? $"Seu horário em {nomeEmpresa} está marcado e confirmado para {quando.Longo}."
                    : $"Seu horário em {nomeEmpresa} está marcado para {quando.Longo}.",
                d.Confirmado ? null : linkConfirmar),
        };

        var itens = d.Itens.Count > 0
            ? d.Itens
            : d.Servicos.Select(n => new ItemNotificacao { Nome = n }).ToList();
        var total = itens.Sum(i => i.Preco);
        var profissional = string.IsNullOrWhiteSpace(d.Profissional) ? null : d.Profissional.Trim();

        var valores = Variaveis(evento, d, empresa, nomeEmpresa, primeiroNome, quando, antes, itens, profissional);
        if (textos?.Assunto is { } assuntoProprio
            && ModelosAviso.Preencher(assuntoProprio, valores).ReplaceLineEndings(" ").Trim() is { Length: > 0 } assuntoPreenchido)
        {
            assunto = assuntoPreenchido;
        }
        if (textos?.Mensagem is { } mensagemPropria
            && ModelosAviso.Preencher(mensagemPropria, valores).Trim() is { Length: > 0 } aberturaPreenchida)
        {
            abertura = aberturaPreenchida;
        }
        var recado = !cancelado && textos?.Recado is { } recadoProprio
            && ModelosAviso.Preencher(recadoProprio, valores).Trim() is { Length: > 0 } recadoPreenchido
                ? recadoPreenchido
                : null;
        var motivo = cancelado && d.Motivo is { } m && !m.Equals("Remarcado", StringComparison.OrdinalIgnoreCase) ? m : null;
        var resumo = itens.Count > 0 ? $"{string.Join(", ", itens.Select(i => i.Nome))} – {nomeEmpresa}" : nomeEmpresa;

        var links = new Links(
            cancelado ? null : GoogleAgenda(d, resumo, profissional, empresa?.Endereco),
            empresa?.Endereco is { } end ? "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(end) : null,
            WhatsApp(empresa?.Telefone));

        var contato = Contato(empresa);
        var fecho = cancelado
            ? $"Para marcar outro horário, {contato}."
            : $"Se precisar mudar alguma coisa, {contato}.";
        var rodape = $"Mensagem automática enviada por {nomeEmpresa} pelo Lyra.";

        var texto = new StringBuilder()
            .AppendLine($"Olá, {primeiroNome}!").AppendLine()
            .AppendLine(abertura).AppendLine();
        if (itens.Count > 0)
        {
            texto.AppendLine(itens.Count == 1 ? "Serviço:" : "Serviços:");
            foreach (var i in itens) texto.AppendLine($"- {i.Nome}{(cancelado ? "" : DetalheItem(i))}");
            if (!cancelado && total > 0 && itens.Count > 1) texto.AppendLine($"Total: {Dinheiro(total)}");
        }
        if (profissional is not null) texto.AppendLine($"Com: {profissional}");
        texto.AppendLine($"Quando: {quando.Completo}{(cancelado ? " (cancelado)" : "")}");
        if (antes is not null) texto.AppendLine($"Antes: {Capitalizar(antes.Longo)}");
        if (empresa?.Endereco is { } endereco) texto.AppendLine($"Onde: {endereco}");
        if (motivo is not null) texto.AppendLine($"Motivo: {motivo}");
        texto.AppendLine();
        if (recado is not null) texto.AppendLine(recado).AppendLine();
        if (botao is not null) texto.AppendLine($"Confirme sua presença: {botao}");
        if (links.Agenda is not null) texto.AppendLine($"Adicionar ao Google Agenda: {links.Agenda}");
        if (links.Mapa is not null && !cancelado) texto.AppendLine($"Como chegar: {links.Mapa}");
        if (links.WhatsApp is not null) texto.AppendLine($"WhatsApp: {links.WhatsApp}");
        texto.AppendLine().AppendLine(fecho).AppendLine().AppendLine(rodape)
            .AppendLine($"Não quer mais receber estes e-mails? {MarcadorDescadastro}");

        var uid = d.Cadeia > 0 ? d.Cadeia : agendamentoId;
        // O lembrete não manda convite: o evento já foi para o calendário quando o horário foi marcado.
        var ics = evento == EventosNotificacao.AgendamentoLembrete ? null : Ics(evento, uid, d, resumo, profissional, empresa?.Endereco);
        var html = Html(new Pagina(
            Estilo(evento), cancelado, ics is not null && !cancelado, nomeEmpresa, empresa, titulo, $"Olá, {primeiroNome}!", abertura,
            quando, antes, itens, total, profissional, motivo, recado, botao, links, fecho, rodape));
        return new EmailMontado(assunto, html, texto.ToString(), ics);
    }

    private static (string Assunto, string Titulo, string Abertura, string? Botao) Lembrete(
        DadosNotificacao d, string nomeEmpresa, Momento quando, string? linkConfirmar)
    {
        var botao = d.Confirmado ? null : linkConfirmar;
        if (quando.Data is not { } dt) return (
            $"Lembrete: {quando.Curto} – {nomeEmpresa}",
            "Lembrete do seu horário",
            $"Passando para lembrar do seu horário em {nomeEmpresa}: {quando.Longo}.",
            botao);

        var (dia, quandoTexto) = Relativo(d, quando);
        var titulo = dia switch
        {
            "hoje" => "Seu horário é hoje",
            "amanhã" => "Seu horário é amanhã",
            _ => "Lembrete do seu horário",
        };
        return (
            $"Lembrete: {dia} às {dt.ToString("HH:mm", PtBr)} – {nomeEmpresa}",
            titulo,
            $"Passando para lembrar: seu horário em {nomeEmpresa} é {quandoTexto}.",
            botao);
    }

    /// <summary>
    /// "hoje", "amanhã" ou a data, contando a partir de quando o aviso foi programado (o lembrete) ou de agora;
    /// e o dia e a hora por extenso nessa mesma linguagem.
    /// </summary>
    private static (string Dia, string Quando) Relativo(DadosNotificacao d, Momento quando)
    {
        if (quando.Data is not { } dt) return (quando.Curto, quando.Longo);
        var fuso = FusoHorarioEmpresa.Resolver(string.IsNullOrEmpty(d.Fuso) ? Segmentos.FusoPadrao : d.Fuso);
        var hoje = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d.Referencia ?? DateTime.UtcNow, DateTimeKind.Utc), fuso).Date;
        var hora = dt.ToString("HH:mm", PtBr);
        return (dt.Date - hoje).Days switch
        {
            0 => ("hoje", $"hoje às {hora}"),
            1 => ("amanhã", $"amanhã ({dt.ToString("dddd", PtBr)}) às {hora}"),
            _ => (dt.ToString("dd/MM", PtBr), quando.Longo),
        };
    }

    /// <summary>Os valores de <see cref="ModelosAviso.Variaveis"/> para este e-mail.</summary>
    private static Dictionary<string, string?> Variaveis(
        string evento, DadosNotificacao d, EmpresaRemetente? empresa, string nomeEmpresa, string primeiroNome,
        Momento quando, Momento? antes, IReadOnlyList<ItemNotificacao> itens, string? profissional)
    {
        var (dia, relativo) = Relativo(d, quando);
        var nomes = itens.Select(i => i.Nome).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        return new Dictionary<string, string?>
        {
            ["cliente"] = primeiroNome,
            ["servico"] = nomes.Count <= 1 ? nomes.FirstOrDefault() : $"{string.Join(", ", nomes.Take(nomes.Count - 1))} e {nomes[^1]}",
            ["profissional"] = profissional,
            ["data"] = quando.Data?.ToString("dd/MM", PtBr) ?? quando.Curto,
            ["hora"] = quando.Data?.ToString("HH:mm", PtBr) ?? "",
            ["dia"] = dia,
            ["quando"] = evento == EventosNotificacao.AgendamentoLembrete ? relativo : quando.Longo,
            ["quando_anterior"] = antes?.Longo,
            ["empresa"] = nomeEmpresa,
            ["endereco"] = empresa?.Endereco,
            ["telefone"] = empresa?.Telefone,
        };
    }

    private static string Contato(EmpresaRemetente? e) => (e?.Email, e?.Telefone) switch
    {
        (not null, not null) => $"responda este e-mail ou ligue para {e!.Telefone}",
        (not null, null) => "é só responder este e-mail",
        (null, not null) => $"fale com a gente pelo {e!.Telefone}",
        _ => "fale com a gente",
    };

    /// <param name="Completo">Dia, início, fim e duração: "Quarta-feira, 18/11 · 14:30 às 16:00 (1h30)".</param>
    private sealed record Momento(string Curto, string Longo, string Completo, DateTime? Data, string Horario);

    private static Momento Quando(string local, int minutos)
    {
        if (!DateTime.TryParseExact(local, "dd/MM/yyyy HH:mm", PtBr, DateTimeStyles.None, out var dt))
        {
            return new Momento(local, local, local, null, local);
        }
        var horario = minutos > 0
            ? $"{dt:HH:mm} às {dt.AddMinutes(minutos):HH:mm} · {Duracao(minutos)}"
            : dt.ToString("HH:mm", PtBr);
        return new Momento(
            dt.ToString("dd/MM 'às' HH:mm", PtBr),
            dt.ToString("dddd, dd/MM 'às' HH:mm", PtBr),
            $"{Capitalizar(dt.ToString("dddd, dd/MM", PtBr))} · {horario}",
            dt,
            horario);
    }

    private static string Duracao(int minutos) => (minutos / 60, minutos % 60) switch
    {
        (0, var m) => $"{m} min",
        (var h, 0) => $"{h}h",
        var (h, m) => $"{h}h{m:00}",
    };

    private static string Dinheiro(decimal valor) => valor.ToString("C", PtBr);

    /// <summary>" (45 min) – R$ 80,00", sem as partes que faltam.</summary>
    private static string DetalheItem(ItemNotificacao i) =>
        (i.DuracaoMinutos > 0 ? $" ({Duracao(i.DuracaoMinutos)})" : "") + (i.Preco > 0 ? $" – {Dinheiro(i.Preco)}" : "");

    private static string Capitalizar(string s) => s.Length == 0 ? s : char.ToUpper(s[0], PtBr) + s[1..];

    private sealed record Links(string? Agenda, string? Mapa, string? WhatsApp);

    private static string GoogleAgenda(DadosNotificacao d, string resumo, string? profissional, string? endereco)
    {
        var fim = d.Fim > d.Inicio ? d.Fim : d.Inicio.AddMinutes(30);
        var url = new StringBuilder("https://calendar.google.com/calendar/render?action=TEMPLATE")
            .Append("&text=").Append(Uri.EscapeDataString(resumo))
            .Append("&dates=").Append(Utc(d.Inicio)).Append("%2F").Append(Utc(fim));
        if (profissional is not null) url.Append("&details=").Append(Uri.EscapeDataString("Com " + profissional));
        if (endereco is not null) url.Append("&location=").Append(Uri.EscapeDataString(endereco));
        return url.ToString();
    }

    /// <summary>Só celular brasileiro (DDD + 9 dígitos): fixo raramente tem WhatsApp.</summary>
    private static string? WhatsApp(string? telefone)
    {
        if (telefone is null) return null;
        var digitos = new string(telefone.Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length == 13 && digitos.StartsWith("55")) digitos = digitos[2..];
        return digitos.Length == 11 && digitos[2] == '9' ? $"https://wa.me/55{digitos}" : null;
    }

    private static string Utc(DateTime t) =>
        DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Cor e ícone do selo no topo do cartão.</summary>
    private sealed record EstiloEvento(string Selo, string Icone, string Cor, string Fundo);

    private static EstiloEvento Estilo(string evento) => evento switch
    {
        EventosNotificacao.AgendamentoRemarcado => new("Horário alterado", "&#8635;", "#1d4ed8", "#eff6ff"),
        EventosNotificacao.AgendamentoCancelado => new("Cancelado", "&#10005;", "#b91c1c", "#fef2f2"),
        EventosNotificacao.AgendamentoConfirmado => new("Confirmado", "&#10003;", "#15803d", "#f0fdf4"),
        EventosNotificacao.AgendamentoLembrete => new("Lembrete", "&#9200;", "#b45309", "#fffbeb"),
        _ => new("Agendado", "&#10003;", "#9a5b27", "#fbf3ec"),
    };

    private sealed record Pagina(
        EstiloEvento Estilo, bool Cancelado, bool ComConvite, string NomeEmpresa, EmpresaRemetente? Empresa, string Titulo,
        string Saudacao, string Abertura, Momento Quando, Momento? Antes, List<ItemNotificacao> Itens, decimal Total,
        string? Profissional, string? Motivo, string? Recado, string? Botao, Links Links, string Fecho, string Rodape);

    /// <summary>Escapado, com as quebras de linha que a empresa digitou.</summary>
    private static string Paragrafo(string s) => E(s).Replace("\n", "<br>");

    private static string E(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&#39;");

    private static string Iniciais(string nome) => string.Concat(nome
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(p => char.IsLetterOrDigit(p[0]))
        .Take(2)
        .Select(p => char.ToUpper(p[0], PtBr)));

    private const string Fonte = "font-family:'Segoe UI',Roboto,Helvetica,Arial,sans-serif";
    private const string Marca = "#9a5b27";
    private const string Tinta = "#1f1a17";
    private const string Suave = "#8a7f76";
    private const string Linha = "#efe7df";

    private static string Rotulo(string texto) =>
        $"""<div style="font-size:11px;letter-spacing:.08em;text-transform:uppercase;color:{Suave}">{texto}</div>""";

    private static string BotaoSecundario(string href, string icone, string texto, string cor = Tinta) =>
        $"""<a href="{E(href)}" style="display:inline-block;margin:4px;padding:10px 16px;border:1px solid #e2d8ce;border-radius:999px;background:#ffffff;color:{cor};font-size:13px;font-weight:600;text-decoration:none;white-space:nowrap">{icone}&nbsp; {texto}</a>""";

    /// <summary>O logo já traz o nome; sem logo, monograma + nome.</summary>
    private static string Cabecalho(EmpresaRemetente? empresa, string nomeEmpresa) => empresa?.LogoUrl is { } logo
        ? $"""<img src="{E(logo)}" alt="{E(nomeEmpresa)}" height="64" style="display:block;margin:0 auto;height:64px;max-width:240px;border:0;outline:none;text-decoration:none">"""
        : $"""
            <table role="presentation" cellpadding="0" cellspacing="0" align="center"><tr><td width="64" height="64" align="center" valign="middle" style="width:64px;height:64px;border-radius:32px;background:{Marca};color:#ffffff;font-size:24px;font-weight:700;{Fonte}">{E(Iniciais(nomeEmpresa))}</td></tr></table>
            <div style="margin-top:10px;font-size:16px;font-weight:600;color:#3d332c">{E(nomeEmpresa)}</div>
            """;

    // Layout em tabelas e estilos inline: é o que Gmail, Outlook e Apple Mail renderizam igual.
    private static string Html(Pagina p)
    {
        var s = p.Estilo;
        var cabecalho = Cabecalho(p.Empresa, p.NomeEmpresa);

        // Bloco de data: "18 / NOV" ao lado do dia da semana e do horário.
        var riscado = p.Cancelado ? "text-decoration:line-through;" : "";
        var data = p.Quando.Data is { } dt
            ? $"""
                <td width="72" valign="top" style="width:72px">
                <table role="presentation" width="72" cellpadding="0" cellspacing="0" style="border:1px solid {Linha};border-radius:12px;background:#ffffff">
                <tr><td align="center" style="padding:6px 0;border-radius:11px 11px 0 0;background:{s.Cor};color:#ffffff;font-size:11px;font-weight:700;letter-spacing:.1em">{E(dt.ToString("MMM", PtBr).TrimEnd('.').ToUpper(PtBr))}</td></tr>
                <tr><td align="center" style="padding:6px 0 8px;font-size:28px;font-weight:700;line-height:1;color:{Tinta};{riscado}">{dt:dd}</td></tr>
                </table>
                </td>
                <td valign="middle" style="padding-left:16px">
                <div style="font-size:17px;font-weight:700;color:{Tinta};{riscado}">{E(Capitalizar(dt.ToString("dddd", PtBr)))}</div>
                <div style="margin-top:2px;font-size:15px;color:#3d332c;{riscado}">{E(p.Quando.Horario)}</div>
                </td>
                """
            : $"""<td style="font-size:16px;font-weight:600;color:{Tinta};{riscado}">{E(p.Quando.Completo)}</td>""";
        var antes = p.Antes is null ? "" : $"""
            <tr><td colspan="2" style="padding:0 0 14px">{Rotulo("Antes era")}<div style="margin-top:3px;font-size:14px;color:{Suave};text-decoration:line-through">{E(Capitalizar(p.Antes.Longo))}</div></td></tr>
            """;

        var detalhes = new StringBuilder();
        void Detalhe(string icone, string rotulo, string valorHtml) => detalhes.Append($"""
            <tr><td style="padding:12px 0 0;border-top:1px solid {Linha}">
            <table role="presentation" cellpadding="0" cellspacing="0"><tr>
            <td width="28" valign="top" style="width:28px;font-size:16px;line-height:20px">{icone}</td>
            <td valign="top">{Rotulo(rotulo)}<div style="margin-top:2px;font-size:15px;font-weight:600;color:{Tinta}">{valorHtml}</div></td>
            </tr></table>
            </td></tr>
            <tr><td style="height:12px;line-height:12px;font-size:0">&nbsp;</td></tr>
            """);
        if (p.Profissional is not null) Detalhe("&#128100;", "Com", E(p.Profissional));
        if (p.Empresa?.Endereco is { } endereco) Detalhe("&#128205;", "Onde", E(endereco));
        if (p.Motivo is not null) Detalhe("&#128172;", "Motivo", E(p.Motivo));

        var servicos = "";
        if (p.Itens.Count > 0)
        {
            var temPreco = !p.Cancelado && p.Itens.Any(i => i.Preco > 0);
            var linhas = new StringBuilder();
            foreach (var i in p.Itens)
            {
                var duracao = i.DuracaoMinutos > 0 ? $"""<div style="margin-top:2px;font-size:12px;color:{Suave}">{Duracao(i.DuracaoMinutos)}</div>""" : "";
                var preco = temPreco ? $"""<td align="right" valign="top" style="padding:10px 0;border-top:1px solid {Linha};font-size:14px;color:{Tinta};white-space:nowrap">{(i.Preco > 0 ? E(Dinheiro(i.Preco)) : "")}</td>""" : "";
                linhas.Append($"""<tr><td valign="top" style="padding:10px 12px 10px 0;border-top:1px solid {Linha};font-size:14px;font-weight:600;color:{Tinta}">{E(i.Nome)}{duracao}</td>{preco}</tr>""");
            }
            if (temPreco && p.Itens.Count > 1)
            {
                linhas.Append($"""<tr><td style="padding:12px 12px 0 0;border-top:2px solid {Linha};font-size:14px;font-weight:700;color:{Tinta}">Total</td><td align="right" style="padding:12px 0 0;border-top:2px solid {Linha};font-size:16px;font-weight:700;color:{Tinta};white-space:nowrap">{E(Dinheiro(p.Total))}</td></tr>""");
            }
            servicos = $"""
                <tr><td style="padding:12px 32px 8px">
                {Rotulo(p.Itens.Count == 1 ? "&#9986;&nbsp; Serviço" : "&#9986;&nbsp; Serviços")}
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:6px">{linhas}</table>
                </td></tr>
                """;
        }

        var recado = p.Recado is null ? "" : $"""
            <tr><td style="padding:16px 32px 4px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#fbf3ec;border:1px solid #f0dcc8;border-radius:12px">
            <tr><td style="padding:14px 16px;font-size:14px;line-height:1.55;color:#3d332c">{Paragrafo(p.Recado)}</td></tr>
            </table>
            </td></tr>
            """;

        var acao = p.Botao is null ? "" : $"""
            <tr><td align="center" style="padding:16px 32px 4px">
            <a href="{E(p.Botao)}" style="display:inline-block;background:{Marca};color:#ffffff;text-decoration:none;font-size:16px;font-weight:600;padding:14px 36px;border-radius:10px">Confirmar presença</a>
            </td></tr>
            """;

        var atalhos = new List<string>();
        if (p.Links.Agenda is { } agenda) atalhos.Add(BotaoSecundario(agenda, "&#128197;", "Google Agenda"));
        if (p.Links.Mapa is { } urlMapa2 && !p.Cancelado) atalhos.Add(BotaoSecundario(urlMapa2, "&#128205;", "Como chegar"));
        if (p.Links.WhatsApp is { } zap) atalhos.Add(BotaoSecundario(zap, "&#128172;", p.Cancelado ? "Remarcar pelo WhatsApp" : "WhatsApp", "#128c4a"));
        var linhaAtalhos = atalhos.Count == 0 ? "" : $"""<tr><td align="center" style="padding:12px 28px 0">{string.Concat(atalhos)}</td></tr>""";
        var convite = !p.ComConvite ? "" :
            $"""<tr><td align="center" style="padding:10px 32px 0;font-size:12px;color:{Suave}">O convite em anexo (.ics) também coloca este horário na agenda do celular.</td></tr>""";

        var contatos = new[] { p.Empresa?.Telefone, p.Empresa?.Email }.Where(c => c is not null).Select(c => E(c!)).ToArray();
        var contato = contatos.Length == 0 ? "" :
            $"""<div style="margin-top:4px">{string.Join(" &nbsp;&middot;&nbsp; ", contatos)}</div>""";
        var enderecoRodape = p.Empresa?.Endereco is { } end ? $"""<div style="margin-top:4px">{E(end)}</div>""" : "";
        var fecho = p.Empresa?.Telefone is { } tel
            ? E(p.Fecho).Replace(E(tel), $"""<span style="white-space:nowrap">{E(tel)}</span>""")
            : E(p.Fecho);

        return $"""
            <!doctype html>
            <html lang="pt-BR">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"><title>{E(p.Titulo)}</title></head>
            <body style="margin:0;padding:0;background:#f5f0eb;{Fonte}">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0">{E(p.Abertura.ReplaceLineEndings(" "))}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f5f0eb">
            <tr><td align="center" style="padding:32px 12px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px">
            <tr><td align="center" style="padding:0 0 20px">
            {cabecalho}
            </td></tr>
            <tr><td style="background:#ffffff;border-radius:16px;box-shadow:0 1px 3px rgba(31,26,23,.08)">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
            <tr><td height="6" style="height:6px;line-height:6px;font-size:0;background:{s.Cor};border-radius:16px 16px 0 0">&nbsp;</td></tr>
            <tr><td align="center" style="padding:28px 32px 0">
            <span style="display:inline-block;background:{s.Fundo};color:{s.Cor};font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;padding:6px 14px;border-radius:999px">{s.Icone}&nbsp; {E(s.Selo)}</span>
            <h1 style="margin:16px 0 0;font-size:24px;line-height:1.25;color:{Tinta}">{E(p.Titulo)}</h1>
            </td></tr>
            <tr><td style="padding:20px 32px 0;font-size:15px;line-height:1.6;color:#3d332c">
            <p style="margin:0 0 6px">{E(p.Saudacao)}</p>
            <p style="margin:0">{Paragrafo(p.Abertura)}</p>
            </td></tr>
            <tr><td style="padding:24px 32px 8px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#fbf8f5;border:1px solid {Linha};border-radius:14px">
            <tr><td style="padding:18px 20px">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
            {antes}
            <tr>{data}</tr>
            </table>
            </td></tr>
            </table>
            </td></tr>
            <tr><td style="padding:8px 32px 0">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">{detalhes}</table>
            </td></tr>
            {servicos}
            {recado}
            {acao}
            {linhaAtalhos}
            {convite}
            <tr><td style="padding:24px 32px 32px;font-size:14px;line-height:1.6;color:#5c514a">{fecho}</td></tr>
            </table>
            </td></tr>
            <tr><td align="center" style="padding:20px 24px 0;font-size:12px;line-height:1.5;color:{Suave}">
            <div style="font-weight:600;color:#5c514a">{E(p.NomeEmpresa)}</div>
            {contato}
            {enderecoRodape}
            <div style="margin-top:12px;color:#a59a91">{E(p.Rodape)}</div>
            <div style="margin-top:6px"><a href="{MarcadorDescadastro}" style="color:#a59a91;text-decoration:underline">Não quero mais receber estes e-mails</a></div>
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body></html>
            """;
    }

    /// <summary>Convite de calendário. Mesmo UID para toda a cadeia de remarcações, então o evento é atualizado em vez de duplicado.</summary>
    private static string Ics(string evento, int uid, DadosNotificacao d, string resumo, string? profissional, string? endereco)
    {
        var cancelado = evento == EventosNotificacao.AgendamentoCancelado;
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
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
        if (profissional is not null) sb.Append($"DESCRIPTION:{Esc("Com " + profissional)}\r\n");
        if (endereco is not null) sb.Append($"LOCATION:{Esc(endereco)}\r\n");
        sb.Append(cancelado ? "STATUS:CANCELLED\r\n" : "STATUS:CONFIRMED\r\n")
            .Append("END:VEVENT\r\n")
            .Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }
}
