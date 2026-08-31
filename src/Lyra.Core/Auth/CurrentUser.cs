namespace Lyra.Core.Auth;

public sealed record CurrentUser(
    int? UserId,
    string? Username,
    string? Email,
    int? EmpresaId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Modulos)
{
    public bool IsAuthenticated => UserId is > 0;

    public bool IsSuperAdmin =>
        Roles.Any(r => r.Replace("-", "").Replace("_", "").Equals("SUPERADMIN", StringComparison.OrdinalIgnoreCase));

    public bool HasModulo(string codigo)
    {
        if (IsSuperAdmin) return true;
        var want = codigo.Trim().ToUpperInvariant();
        return Modulos.Any(m => m.Trim().ToUpperInvariant() == want);
    }

    public bool HasRole(string role) =>
        Roles.Any(r => r.Equals(role, StringComparison.OrdinalIgnoreCase));
}
