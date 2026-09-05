namespace Lyra.Core.Modules;

/// <summary>
/// Códigos de módulo do Lyra. Devem existir em core.modulos.
/// Raiz de segurança: LYRA000000 (legado LYR0000000 ainda aceito).
/// </summary>
public static class ModuleCodes
{
    public const string Raiz = "LYRA000000";
    public const string RaizLegado = "LYR0000000";
    public const string Agenda = "LYR0000001";
    public const string Servicos = "LYR0000002";
    public const string Clientes = "LYR0000003";
    public const string Profissionais = "LYR0000004";
    public const string Configuracoes = "LYR0000005";

    public static readonly string[] Raizes = [Raiz, RaizLegado];

    public static readonly string[] Todos =
    [
        Raiz,
        RaizLegado,
        Agenda,
        Servicos,
        Clientes,
        Profissionais,
        Configuracoes
    ];
}
