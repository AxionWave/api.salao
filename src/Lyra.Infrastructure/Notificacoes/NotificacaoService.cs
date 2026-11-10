using Lyra.Application.Abstractions;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Notificacoes;

public sealed class NotificacaoService(LyraDbContext db) : INotificacaoService
{
    private const int LimiteHistorico = 100;

    public async Task<IReadOnlyList<NotificacaoDto>> HistoricoClienteAsync(int empresaId, int clienteId, CancellationToken ct)
    {
        var existe = await db.Clientes.AnyAsync(c => c.Id == clienteId && c.EmpresaId == empresaId, ct);
        if (!existe)
        {
            throw new KeyNotFoundException("Cliente não encontrado.");
        }

        return await db.Notificacoes.AsNoTracking()
            .Where(n => n.EmpresaId == empresaId && n.ClienteId == clienteId)
            .OrderByDescending(n => n.DataCriacao)
            .ThenByDescending(n => n.Id)
            .Take(LimiteHistorico)
            .Select(n => new NotificacaoDto(
                n.Id,
                n.Evento,
                n.Canal,
                n.Destino,
                n.AgendamentoId,
                n.Status,
                n.Tentativas,
                n.UltimoErro,
                n.DataCriacao,
                n.DataEnvio))
            .ToListAsync(ct);
    }
}
