using Lyra.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lyra.API.Auth;

/// <summary>Exige que o JWT contenha um dos códigos de módulo (SUPER_ADMIN passa).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireModuloAttribute : Attribute, IAuthorizationFilter
{
    public string[] Codigos { get; }

    public RequireModuloAttribute(params string[] codigos)
    {
        Codigos = codigos ?? [];
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var accessor = context.HttpContext.RequestServices.GetService<ICurrentUserAccessor>();
        var user = accessor?.User;
        if (user is null || !user.IsAuthenticated)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "authentication_required", message = "JWT ausente ou inválido." });
            return;
        }

        if (Codigos.Length == 0 || !Codigos.Any(user.HasModulo))
        {
            var lista = string.Join(" ou ", Codigos);
            context.Result = new ObjectResult(new
            {
                error = "access_denied",
                message = $"Módulo {lista} não está no token. Peça acesso no ASC e faça login novamente."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
