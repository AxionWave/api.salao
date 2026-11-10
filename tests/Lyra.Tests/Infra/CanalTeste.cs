using System.Collections.Concurrent;
using Lyra.Infrastructure.Notificacoes;
using Lyra.Infrastructure.Persistence.Entities;

namespace Lyra.Tests.Infra;

/// <summary>Canal falso: “envia” para o telefone do cliente e falha para os destinos marcados.</summary>
public sealed class CanalTeste : ICanalNotificacao
{
    public const string NomeCanal = "teste";

    public string Nome => NomeCanal;

    public ConcurrentDictionary<string, byte> DestinosComFalha { get; } = new();

    public ConcurrentBag<long> Enviadas { get; } = [];

    public string? Destino(Cliente cliente) => string.IsNullOrWhiteSpace(cliente.Telefone) ? null : cliente.Telefone;

    public Task<string?> EnviarAsync(Notificacao n, CancellationToken ct)
    {
        if (n.Destino is not null && DestinosComFalha.ContainsKey(n.Destino))
        {
            throw new InvalidOperationException("Provedor fora do ar (simulado).");
        }
        Enviadas.Add(n.Id);
        return Task.FromResult<string?>($"teste-{n.Id}");
    }
}
