using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Lyra.API.Exceptions;

/** Catálogo de unicidade. Campo novo: acrescente um if aqui. */
public static class MensagensUnicidade
{
    public static string De(DbUpdateException ex)
    {
        var texto = ex.InnerException?.Message ?? ex.Message;
        if (ex.InnerException is PostgresException pg)
        {
            texto = $"{pg.ConstraintName} {pg.Detail} {pg.MessageText}";
        }

        var lower = texto.ToLowerInvariant();
        if (lower.Contains("cpf")) return "Este CPF já está cadastrado.";
        if (lower.Contains("telefone")) return "Este telefone já está cadastrado nesta empresa.";
        if (lower.Contains("email")) return "Este e-mail já está em uso nesta empresa.";
        if (lower.Contains("pessoa")) return "Esta pessoa já está vinculada como profissional nesta empresa.";
        if (lower.Contains("nome")) return "Já existe um cadastro com este nome nesta empresa.";
        return "Estes dados já estão cadastrados.";
    }
}
