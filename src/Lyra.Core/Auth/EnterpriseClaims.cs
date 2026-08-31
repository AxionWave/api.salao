namespace Lyra.Core.Auth;

/// <summary>Nomes dos claims emitidos pelo oAuth Enterprise (mesmo JWT do Gateway).</summary>
public static class EnterpriseClaims
{
    public const string UserId = "userId";
    public const string Username = "username";
    public const string Email = "email";
    public const string EmpresaId = "empresaId";
    public const string Roles = "roles";
    public const string Modulos = "modulos";
}
