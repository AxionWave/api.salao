using System.Text.Json;
using System.Text.RegularExpressions;
using Lyra.Application.Notificacoes;

namespace Lyra.Application.Configuracao;

/// <param name="Cliente">E-mail ao cliente neste evento (vale só com <see cref="AvisosDto.EnviarEmails"/> ligado).</param>
/// <param name="Equipe">Push no celular da equipe. O sino do painel mostra sempre.</param>
/// <param name="Assunto">Nulo = texto padrão.</param>
/// <param name="Mensagem">Parágrafo de abertura do e-mail. Nulo = texto padrão.</param>
public sealed record AvisoEventoDto(string Evento, bool Cliente, bool Equipe, string? Assunto, string? Mensagem);

/// <param name="Recado">Nota fixa em todo e-mail ao cliente, menos o de cancelamento (ex.: "chegue 10 minutos antes").</param>
public sealed record AvisosDto(
    bool EnviarEmails,
    IReadOnlyList<int> LembretesHoras,
    bool ResumoDiario,
    IReadOnlyList<AvisoEventoDto> Eventos,
    string? Recado);

/// <param name="Equipe">O evento também avisa a equipe por push (o lembrete é só para o cliente).</param>
public sealed record ModeloAvisoDto(string Evento, string Nome, string Descricao, string Assunto, string Mensagem, bool Equipe);

public sealed record VariavelAvisoDto(string Nome, string Descricao, string Exemplo);

public sealed record AvisosResponse(AvisosDto Avisos, IReadOnlyList<ModeloAvisoDto> Modelos, IReadOnlyList<VariavelAvisoDto> Variaveis);

/// <summary>Textos ainda não salvos: a prévia mostra o e-mail como sairia com eles.</summary>
public sealed record PreviaAvisoRequest(string Evento, string? Assunto, string? Mensagem, string? Recado);

public sealed record PreviaAvisoResponse(string Assunto, string Html, string Texto);

public interface IAvisosService
{
    Task<AvisosResponse> ObterAsync(int empresaId, CancellationToken ct);
    Task<AvisosResponse> SalvarAsync(int empresaId, AvisosDto avisos, CancellationToken ct);
    Task<PreviaAvisoResponse> PreviaAsync(int empresaId, PreviaAvisoRequest pedido, CancellationToken ct);
}

/// <summary>O que fica gravado em <c>empresa_config.avisos</c> (JSON). Evento ausente = tudo ligado, texto padrão.</summary>
public sealed class AvisosEmpresa
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Dictionary<string, AvisoEvento> Eventos { get; init; } = [];
    public string? Recado { get; init; }

    public sealed class AvisoEvento
    {
        public bool Cliente { get; init; } = true;
        public bool Equipe { get; init; } = true;
        public string? Assunto { get; init; }
        public string? Mensagem { get; init; }
    }

    public AvisoEvento Evento(string evento) => Eventos.GetValueOrDefault(evento) ?? new AvisoEvento();

    /// <summary>O lembrete se liga e desliga pelos horários (<c>lembretes_horas</c>), não por aqui.</summary>
    public bool Cliente(string evento) => evento == EventosNotificacao.AgendamentoLembrete || Evento(evento).Cliente;

    public bool Equipe(string evento) => Evento(evento).Equipe;

    public static AvisosEmpresa Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AvisosEmpresa();
        try
        {
            return JsonSerializer.Deserialize<AvisosEmpresa>(json, Json) ?? new AvisosEmpresa();
        }
        catch (JsonException)
        {
            return new AvisosEmpresa();
        }
    }

    public string Serializar() => JsonSerializer.Serialize(this, Json);
}

/// <summary>Os eventos configuráveis, seus textos padrão e as variáveis que os textos aceitam.</summary>
public static partial class ModelosAviso
{
    public const int MaxAssunto = 150;
    public const int MaxMensagem = 1000;
    public const int MaxRecado = 500;

    /// <summary>Os mesmos textos que o e-mail usa quando a empresa não personaliza.</summary>
    public static readonly IReadOnlyList<ModeloAvisoDto> Todos =
    [
        new(EventosNotificacao.AgendamentoCriado, "Horário marcado",
            "Quando um horário é marcado pela equipe.",
            "Horário marcado: {data} às {hora} – {empresa}",
            "Seu horário em {empresa} está marcado para {quando}.", Equipe: true),
        new(EventosNotificacao.AgendamentoRemarcado, "Horário alterado",
            "Quando o horário muda de dia, hora ou profissional.",
            "Horário alterado: {data} às {hora} – {empresa}",
            "Seu horário em {empresa} passou de {quando_anterior} para {quando}.", Equipe: true),
        new(EventosNotificacao.AgendamentoCancelado, "Horário cancelado",
            "Quando o horário é cancelado.",
            "Horário cancelado: {data} às {hora} – {empresa}",
            "O horário de {quando} em {empresa} foi cancelado.", Equipe: true),
        new(EventosNotificacao.AgendamentoConfirmado, "Presença confirmada",
            "Quando a equipe confirma a presença. Quem confirma pelo link do e-mail já vê a confirmação na tela.",
            "Presença confirmada: {data} às {hora} – {empresa}",
            "Está confirmado: esperamos você em {empresa} {quando}.", Equipe: true),
        new(EventosNotificacao.AgendamentoLembrete, "Lembrete",
            "Algumas horas antes do horário, para quem ainda vem.",
            "Lembrete: {dia} às {hora} – {empresa}",
            "Passando para lembrar: seu horário em {empresa} é {quando}.", Equipe: false),
    ];

    public static readonly IReadOnlyList<VariavelAvisoDto> Variaveis =
    [
        new("cliente", "Primeiro nome do cliente", "Maria"),
        new("servico", "Serviços do horário", "Corte e escova"),
        new("profissional", "Quem atende", "Ana"),
        new("data", "Data", "24/11"),
        new("hora", "Hora de início", "15:00"),
        new("dia", "\"hoje\", \"amanhã\" ou a data", "amanhã"),
        new("quando", "Dia e hora por extenso", "terça-feira, 24/11 às 15:00"),
        new("quando_anterior", "Dia e hora de antes (só na remarcação)", "segunda-feira, 23/11 às 10:00"),
        new("empresa", "Nome da empresa", "Salão Bela"),
        new("endereco", "Endereço da empresa", "Rua A, 10, São Paulo - SP"),
        new("telefone", "Telefone da empresa", "(11) 3333-4444"),
    ];

    public static ModeloAvisoDto? Obter(string? evento) => Todos.FirstOrDefault(m => m.Evento == evento);

    /// <summary>Troca cada <c>{variavel}</c> pelo valor; variável sem valor vira vazio.</summary>
    public static string Preencher(string texto, IReadOnlyDictionary<string, string?> valores) =>
        Variavel().Replace(texto, m => valores.TryGetValue(m.Groups[1].Value, out var v) ? v ?? "" : m.Value);

    /// <summary>Texto aparado e conferido; vazio ou igual ao padrão = null (segue o padrão, mesmo se ele mudar).</summary>
    /// <exception cref="ArgumentException">Variável desconhecida ou texto longo demais.</exception>
    public static string? Normalizar(string? texto, string? padrao, int maximo, string campo, bool umaLinha = false)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var limpo = texto.Replace("\r\n", "\n").Trim();
        if (umaLinha) limpo = string.Join(' ', limpo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (limpo.Length > maximo) throw new ArgumentException($"{campo}: no máximo {maximo} caracteres.");
        var desconhecidas = Variavel().Matches(limpo)
            .Select(m => m.Groups[1].Value)
            .Where(nome => Variaveis.All(v => v.Nome != nome))
            .Distinct()
            .ToList();
        if (desconhecidas.Count > 0)
        {
            throw new ArgumentException(
                $"{campo}: {(desconhecidas.Count == 1 ? "variável desconhecida" : "variáveis desconhecidas")} " +
                $"{string.Join(", ", desconhecidas.Select(d => "{" + d + "}"))}. Use: {string.Join(", ", Variaveis.Select(v => "{" + v.Nome + "}"))}.");
        }
        return limpo == padrao ? null : limpo;
    }

    [GeneratedRegex(@"\{([a-z_]+)\}")]
    private static partial Regex Variavel();
}
