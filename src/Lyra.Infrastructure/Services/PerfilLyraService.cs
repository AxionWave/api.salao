using Lyra.Application.Abstractions;
using Lyra.Application.Perfis;
using Lyra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Services;

public sealed class PerfilLyraService(LyraDbContext db) : IPerfilLyraService
{
    public async Task<IReadOnlyList<PerfilAcessoLyraDto>> ListarAsync(int empresaId, CancellationToken ct)
    {
        return await db.Database
            .SqlQueryRaw<PerfilAcessoLyraDto>(
                """
                SELECT p.id AS "Id", p.nome AS "Nome", p.descricao AS "Descricao", p.ativo AS "Ativo"
                FROM core.perfis_acesso p
                INNER JOIN core.sistemas s ON s.id = p.sistema_id
                WHERE p.empresa_id = {0}
                  AND p.ativo = TRUE
                  AND UPPER(s.codigo) = 'LYR'
                ORDER BY p.nome
                """,
                empresaId)
            .ToListAsync(ct);
    }
}
