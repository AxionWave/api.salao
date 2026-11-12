using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lyra.Application.Portal;

/// <summary>
/// O que fica em <c>empresa_config.portal</c> (JSON). O endereço fica numa coluna própria, que é única.
/// Ausente = portal desligado, com as regras padrão.
/// </summary>
public sealed partial class PortalEmpresa
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly int[] Passos = [10, 15, 20, 30, 60];

    /// <summary>Caminhos do próprio app ou guardados para o futuro: nenhuma empresa pode usá-los.</summary>
    public static readonly HashSet<string> Reservados = new(StringComparer.Ordinal)
    {
        "login", "primeiro-acesso", "c", "perfil", "inicio", "agenda", "servicos", "clientes", "profissionais",
        "configuracoes", "api", "assets", "icons", "sw", "manifest", "portal", "conta", "minha-conta", "entrar",
        "sair", "cadastro", "admin", "lyra", "app", "www", "ajuda", "suporte", "termos", "privacidade", "static",
        "favicon", "robots", "lumen", "lumensoft",
    };

    public bool Ativo { get; init; }
    public string? Apresentacao { get; init; }
    public int AntecedenciaMinutos { get; init; } = 120;
    public int JanelaDias { get; init; } = 60;
    public int LimiteAbertos { get; init; } = 2;
    public int PassoMinutos { get; init; } = 30;
    /// <summary>Até quantas horas antes o cliente cancela ou remarca sozinho; null = só falando com a empresa.</summary>
    public int? PrazoAlteracaoHoras { get; init; } = 24;
    public List<int> ServicosOcultos { get; init; } = [];
    public List<int> ProfissionaisOcultos { get; init; } = [];

    public static PortalEmpresa Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new PortalEmpresa();
        try
        {
            return JsonSerializer.Deserialize<PortalEmpresa>(json, Json) ?? new PortalEmpresa();
        }
        catch (JsonException)
        {
            return new PortalEmpresa();
        }
    }

    public string Serializar() => JsonSerializer.Serialize(this, Json);

    public PortalConfigDto Dto(string? endereco) => new(
        Ativo, endereco, Apresentacao, AntecedenciaMinutos, JanelaDias, LimiteAbertos, PassoMinutos,
        ServicosOcultos, ProfissionaisOcultos, PrazoAlteracaoHoras);

    /// <summary>Minúsculas, sem acento; nulo se vazio. Lança se o formato não serve.</summary>
    public static string? NormalizarEndereco(string? endereco)
    {
        var e = (endereco ?? "").Trim().ToLowerInvariant();
        if (e.Length == 0) return null;
        if (!FormatoEndereco().IsMatch(e) || e.Contains("--"))
        {
            throw new ArgumentException("Endereço: de 3 a 40 letras, números e hífens, começando e terminando com letra ou número.");
        }
        if (Reservados.Contains(e))
        {
            throw new ArgumentException($"O endereço «{e}» é reservado. Escolha outro.");
        }
        return e;
    }

    /// <summary>"Salão Bela & Cia." vira "salao-bela-cia".</summary>
    public static string? Sugerir(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        var semAcento = new StringBuilder();
        foreach (var c in nome.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) semAcento.Append(c);
        }
        var slug = ForaDoEndereco().Replace(semAcento.ToString().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        if (slug.Length < 3 || Reservados.Contains(slug)) return null;
        return slug;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$")]
    private static partial Regex FormatoEndereco();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex ForaDoEndereco();
}
