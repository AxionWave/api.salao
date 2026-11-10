using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Notificacoes;

public sealed class WorkerNotificacoes(
    IServiceScopeFactory scopes,
    IOptions<NotificacoesOptions> opcoes,
    ILogger<WorkerNotificacoes> logger) : BackgroundService
{
    private const int LotePorRodada = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (opcoes.Value.Canais.Length == 0) return;
        var intervalo = TimeSpan.FromSeconds(Math.Max(1, opcoes.Value.IntervaloSegundos));
        using var timer = new PeriodicTimer(intervalo);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processador = scope.ServiceProvider.GetRequiredService<ProcessadorNotificacoes>();
                int enviadas;
                do
                {
                    enviadas = await processador.ProcessarPendentesAsync(LotePorRodada, stoppingToken);
                } while (enviadas == LotePorRodada && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Falha ao processar a fila de notificações");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
