using Lyra.Application.Notificacoes;
using Lyra.Application.Painel;
using Lyra.Infrastructure.AoVivo;
using Lyra.Infrastructure.Persistence.Entities;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>
/// O sino do painel. A linha em <c>lyra.notificacoes</c> é o próprio aviso (o sino lê de lá); "enviar"
/// é só empurrar para as telas abertas da empresa.
/// </summary>
public sealed class CanalPainel(AvisosAoVivo aoVivo) : ICanalNotificacao
{
    public const string NomeCanal = "painel";

    public string Nome => NomeCanal;

    public bool ParaEquipe => true;

    /// <summary>O sino conta o que mudou na agenda; lembrete é para o cliente.</summary>
    public bool Aceita(string evento, string? origem, EmpresaConfig? config) =>
        evento != EventosNotificacao.AgendamentoLembrete;

    public string? Destino(Cliente cliente) => null;

    public async Task<string?> EnviarAsync(Notificacao n, CancellationToken ct)
    {
        await aoVivo.NotificacaoAsync(n.EmpresaId, Mapear(n), ct);
        return null;
    }

    public static PainelNotificacaoDto Mapear(Notificacao n)
    {
        var dados = FilaNotificacoes.LerDados(n.Dados);
        return new PainelNotificacaoDto(
            n.Id,
            n.Evento,
            n.AgendamentoId,
            dados.Cliente,
            dados.Profissional,
            dados.Inicio,
            dados.InicioLocal,
            dados.InicioAnteriorLocal,
            dados.Motivo,
            dados.Origem,
            dados.AutorId,
            n.DataCriacao);
    }
}
