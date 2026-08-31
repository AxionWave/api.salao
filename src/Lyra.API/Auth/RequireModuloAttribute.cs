using Lyra.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lyra.API.Auth;

/// <summary>Exige que o JWT contenha o código de módulo (SUPER_ADMIN passa).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireModuloAttribute(string codigo) : Attribute, IAuthorizationFilter
{
    public string Codigo { get; } = codigo;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var accessor = context.HttpContext.RequestServices.GetService<ICurrentUserAccessor>();
        var user = accessor?.User;
        if (user is null || !user.IsAuthenticated)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "authentication_required", message = "JWT ausente ou inválido." });
            return;
        }

        if (!user.HasModulo(Codigo))
        {
            context.Result = new ObjectResult(new
            {
                error = "access_denied",
                message = $"Módulo {Codigo} não está no token. Peça acesso no ASC e faça login novamente."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
