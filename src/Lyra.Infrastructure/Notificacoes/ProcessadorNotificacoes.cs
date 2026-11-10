using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>Envia as notificações pendentes, uma por transação, e agenda novas tentativas quando falham.</summary>
public sealed class ProcessadorNotificacoes(
    LyraDbContext db,
    IEnumerable<ICanalNotificacao> canais,
    ILogger<ProcessadorNotificacoes> logger)
{
    /// <summary>Espera antes de cada nova tentativa; esgotadas, a notificação falha de vez.</summary>
    public static readonly TimeSpan[] Esperas =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
    ];

    public async Task<int> ProcessarPendentesAsync(int limite, CancellationToken ct)
    {
        var processadas = 0;
        while (processadas < limite)
        {
            var agora = DateTime.UtcNow;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // SKIP LOCKED: várias instâncias da API dividem a fila sem mandar a mesma mensagem duas vezes.
            var item = await db.Notificacoes
                .FromSqlInterpolated($"""
                    SELECT * FROM lyra.notificacoes
                    WHERE status = 'pendente' AND proxima_tentativa <= {agora}
                    ORDER BY proxima_tentativa, id
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .FirstOrDefaultAsync(ct);
            if (item is null) break;

            await EnviarAsync(item, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            processadas++;
        }
        return processadas;
    }

    private async Task EnviarAsync(Notificacao item, CancellationToken ct)
    {
        item.Tentativas++;
        var canal = canais.FirstOrDefault(c => string.Equals(c.Nome, item.Canal, StringComparison.OrdinalIgnoreCase));
        if (canal is null)
        {
            Falhar(item, $"O canal '{item.Canal}' não está disponível nesta instalação.", definitivo: true);
            return;
        }
        try
        {
            item.ProvedorId = await canal.EnviarAsync(item, ct);
            item.Status = StatusNotificacao.Enviado;
            item.DataEnvio = DateTime.UtcNow;
            item.UltimoErro = null;
        }
        catch (NotificacaoRecusadaException ex)
        {
            Falhar(item, ex.Message, definitivo: true, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Falhar(item, ex.Message, definitivo: item.Tentativas > Esperas.Length, ex);
        }
    }

    private void Falhar(Notificacao item, string erro, bool definitivo, Exception? ex = null)
    {
        item.UltimoErro = erro.Length > 500 ? erro[..500] : erro;
        if (definitivo)
        {
            item.Status = StatusNotificacao.Falhou;
            logger.LogError(ex, "Notificação {Id} ({Canal}, {Evento}) falhou de vez após {Tentativas} tentativa(s)",
                item.Id, item.Canal, item.Evento, item.Tentativas);
            return;
        }
        item.ProximaTentativa = DateTime.UtcNow + Esperas[item.Tentativas - 1];
        logger.LogWarning(ex, "Notificação {Id} ({Canal}) falhou na tentativa {Tentativas}; nova tentativa em {Proxima:O}",
            item.Id, item.Canal, item.Tentativas, item.ProximaTentativa);
    }
}
