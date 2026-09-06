using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Lyra.API.Exceptions;

public sealed class ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, error, message) = Map(context.Exception);
        if (status >= 500)
        {
            logger.LogError(context.Exception, "Erro não tratado na API Lyra");
        }
        else
        {
            logger.LogWarning(context.Exception, "Falha de negócio na API Lyra");
        }

        context.Result = new ObjectResult(new { error, message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }

    private static (int Status, string Error, string Message) Map(Exception ex)
    {
        if (ex is ArgumentException)
        {
            return ((int)HttpStatusCode.BadRequest, "validation_error", ex.Message);
        }
        if (ex is KeyNotFoundException)
        {
            return ((int)HttpStatusCode.NotFound, "not_found", "Registro não encontrado.");
        }
        if (ex is InvalidOperationException)
        {
            return ((int)HttpStatusCode.Conflict, "conflict", ex.Message);
        }
        if (ex is DbUpdateException db)
        {
            return ((int)HttpStatusCode.Conflict, "conflict", MensagensUnicidade.De(db));
        }

        return ((int)HttpStatusCode.InternalServerError, "internal_error", "Não foi possível concluir a operação. Tente novamente.");
    }
}
