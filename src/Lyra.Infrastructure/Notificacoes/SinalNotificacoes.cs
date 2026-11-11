namespace Lyra.Infrastructure.Notificacoes;

/// <summary>Acorda o worker logo depois de um commit com notificações, sem esperar o próximo intervalo.</summary>
public sealed class SinalNotificacoes
{
    private readonly SemaphoreSlim sinal = new(0, 1);

    public void Acordar()
    {
        try
        {
            sinal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Já havia um aviso pendente: o worker vai rodar de qualquer jeito.
        }
    }

    /// <summary>Espera um aviso ou o intervalo, o que vier primeiro.</summary>
    public Task<bool> EsperarAsync(TimeSpan intervalo, CancellationToken ct) => sinal.WaitAsync(intervalo, ct);
}
