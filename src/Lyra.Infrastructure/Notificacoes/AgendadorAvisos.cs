using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>Uma vez por minuto, vê se chegou a hora de algum lembrete ou resumo do dia.</summary>
public sealed class AgendadorAvisos(
    IServiceScopeFactory scopes,
    IOptions<NotificacoesOptions> opcoes,
    ILogger<AgendadorAvisos> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (opcoes.Value.Canais.Length == 0) return;
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ProgramadorAvisos>()
                    .ProgramarAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Falha ao programar lembretes e resumos");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
