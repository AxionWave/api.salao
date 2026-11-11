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
            .Where(n => n.EmpresaId == empresaId && n.ClienteId == clienteId
                && n.Canal != CanalPainel.NomeCanal && n.Canal != Push.CanalPush.NomeCanal)
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

    public async Task AplicarAvisoMensageriaAsync(int empresaId, AvisoMensageriaDto aviso, CancellationToken ct)
    {
        var chave = aviso.Chave ?? "";
        if (!chave.StartsWith(CanalEmail.PrefixoChave, StringComparison.Ordinal)
            || !long.TryParse(chave[CanalEmail.PrefixoChave.Length..], out var id))
        {
            throw new KeyNotFoundException("Chave que não é de notificação do Lyra.");
        }
        // O id do envio confere com o que a Mensageria devolveu. Sem id ainda, o worker está gravando a resposta
        // do pedido (a linha está travada e este UPDATE espera por ele).
        var n = await db.Notificacoes.FirstOrDefaultAsync(
                x => x.Id == id && x.EmpresaId == empresaId && (x.ProvedorId == null || x.ProvedorId == aviso.Id), ct)
            ?? throw new KeyNotFoundException("Notificação não encontrada.");

        switch (aviso.Evento)
        {
            case "entregue":
                // Devolução que chegou antes vale mais que a entrega.
                if (n.Status == StatusNotificacao.Falhou) return;
                n.Status = StatusNotificacao.Entregue;
                n.UltimoErro = null;
                break;
            case "devolvido":
                n.Status = StatusNotificacao.Falhou;
                n.UltimoErro = Limitar(aviso.Erro ?? "O e-mail voltou: o endereço do cliente não recebe mensagens.");
                break;
            case "falhou":
                n.Status = StatusNotificacao.Falhou;
                n.UltimoErro = Limitar(aviso.Erro ?? "A Mensageria não conseguiu entregar.");
                break;
            case "descadastrado":
                n.Status = StatusNotificacao.Falhou;
                n.UltimoErro = "O cliente pediu para não receber e-mails.";
                break;
            case "reclamacao":
                // O e-mail chegou; o cliente marcou como spam e não recebe os próximos.
                n.UltimoErro = "O cliente marcou o e-mail como spam; os próximos não serão enviados.";
                break;
            default:
                return;
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Limitar(string texto) => texto.Length > 500 ? texto[..500] : texto;
}
