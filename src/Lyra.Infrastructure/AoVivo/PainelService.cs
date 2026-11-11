using Lyra.Application.Abstractions;
using Lyra.Application.Painel;
using Lyra.Infrastructure.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.AoVivo;

public sealed class PainelService(LyraDbContext db) : IPainelService
{
    private const int Limite = 30;
    /// <summary>Quem nunca abriu o sino não começa com o histórico inteiro como novidade.</summary>
    private static readonly TimeSpan JanelaSemLeitura = TimeSpan.FromDays(7);

    public async Task<PainelNotificacoesResponse> ListarAsync(int empresaId, int usuarioId, CancellationToken ct)
    {
        var visto = await db.PainelLeituras.AsNoTracking()
            .Where(l => l.EmpresaId == empresaId && l.UsuarioId == usuarioId)
            .Select(l => (DateTime?)l.VistoAte)
            .FirstOrDefaultAsync(ct)
            ?? DateTime.UtcNow - JanelaSemLeitura;

        // O autor fica no JSON: filtrar no banco exigiria uma coluna só para isso; o lote é pequeno.
        var linhas = await db.Notificacoes.AsNoTracking()
            .Where(n => n.EmpresaId == empresaId && n.Canal == CanalPainel.NomeCanal)
            .OrderByDescending(n => n.DataCriacao)
            .ThenByDescending(n => n.Id)
            .Take(Limite * 2)
            .ToListAsync(ct);
        var itens = linhas
            .Select(CanalPainel.Mapear)
            .Where(a => a.AutorId != usuarioId)
            .Take(Limite)
            .ToList();
        return new PainelNotificacoesResponse(itens, itens.Count(a => a.Data > visto));
    }

    public async Task MarcarVistasAsync(int empresaId, int usuarioId, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO lyra.painel_leituras (empresa_id, usuario_id, visto_ate)
            VALUES ({empresaId}, {usuarioId}, {agora})
            ON CONFLICT (empresa_id, usuario_id) DO UPDATE SET visto_ate = EXCLUDED.visto_ate
            """, ct);
    }
}
