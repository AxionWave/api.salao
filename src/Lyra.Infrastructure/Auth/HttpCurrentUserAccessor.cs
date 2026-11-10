using System.Security.Claims;
using System.Text.Json;
using Lyra.Application.Abstractions;
using Lyra.Core.Auth;
using Microsoft.AspNetCore.Http;

namespace Lyra.Infrastructure.Auth;

/// <summary>
/// Usuário vem só das claims do token assinado. Cabeçalhos como X-Empresa-Id ou X-User-Roles
/// são ignorados: o navegador pode mandá-los com qualquer valor.
/// </summary>
public sealed class HttpCurrentUserAccessor(IHttpContextAccessor http) : ICurrentUserAccessor
{
    public CurrentUser User => FromPrincipal(http.HttpContext?.User);

    public static CurrentUser FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return new CurrentUser(null, null, null, null, [], []);
        }

        var userId = ParseInt(Claim(principal, EnterpriseClaims.UserId));
        var username = Claim(principal, EnterpriseClaims.Username) ?? principal.Identity?.Name;
        var email = Claim(principal, EnterpriseClaims.Email);
        var empresaId = ParseInt(Claim(principal, EnterpriseClaims.EmpresaId));
        var roles = GetList(principal, EnterpriseClaims.Roles);
        var modulos = GetList(principal, EnterpriseClaims.Modulos);

        return new CurrentUser(userId, username, email, empresaId, roles, modulos);
    }

    private static string? Claim(ClaimsPrincipal p, string type) =>
        p.FindFirst(type)?.Value;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var n) ? n : null;

    private static IReadOnlyList<string> GetList(ClaimsPrincipal p, string type)
    {
        var values = p.FindAll(type).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (values.Count == 1 && values[0].TrimStart().StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(values[0]) ?? [];
            }
            catch
            {
                return values;
            }
        }
        return values;
    }
}
