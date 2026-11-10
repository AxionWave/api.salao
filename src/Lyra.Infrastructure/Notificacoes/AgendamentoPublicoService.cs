using Lyra.Application.Abstractions;
using Lyra.Application.Agendamentos;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Lyra.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Notificacoes;

public sealed class AgendamentoPublicoService(
    LyraDbContext db,
    LinksPublicos links,
    LeitorEmpresa empresas,
    FilaNotificacoes fila) : IAgendamentoPublicoService
{
    public async Task<AgendamentoPublicoDto> ObterAsync(string token, CancellationToken ct)
    {
        var agendamento = await CarregarAsync(token, rastrear: false, ct);
        return await MapAsync(agendamento, ct);
    }

    public async Task<AgendamentoPublicoDto> ConfirmarAsync(string token, CancellationToken ct)
    {
        var agendamento = await CarregarAsync(token, rastrear: true, ct);
        if (agendamento.Status != StatusAgendamento.Confirmado)
        {
            if (!PodeConfirmar(agendamento))
            {
                throw new InvalidOperationException(agendamento.Status == StatusAgendamento.Cancelado
                    ? "Este horário foi cancelado."
                    : "Este horário já não pode ser confirmado.");
            }
            agendamento.Status = StatusAgendamento.Confirmado;
            agendamento.DataAtualizacao = DateTime.UtcNow;
            await fila.EnfileirarAsync(agendamento, EventosNotificacao.AgendamentoConfirmado, ct,
                origem: FilaNotificacoes.OrigemCliente);
            await db.SaveChangesAsync(ct);
        }
        return await MapAsync(agendamento, ct);
    }

    private static bool PodeConfirmar(Agendamento a) =>
        a.Status == StatusAgendamento.Agendado && a.Inicio > DateTime.UtcNow;

    private async Task<Agendamento> CarregarAsync(string token, bool rastrear, CancellationToken ct)
    {
        var id = links.Validar(token) ?? throw new KeyNotFoundException("Link inválido.");
        var query = db.Agendamentos.Include(a => a.Itens).AsQueryable();
        if (!rastrear) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new KeyNotFoundException("Agendamento não encontrado.");
    }

    private async Task<AgendamentoPublicoDto> MapAsync(Agendamento a, CancellationToken ct)
    {
        var empresa = await empresas.ObterAsync(a.EmpresaId, ct);
        var cliente = await db.Clientes.AsNoTracking().Where(c => c.Id == a.ClienteId).Select(c => c.Nome).FirstAsync(ct);
        var profissional = await db.Profissionais.AsNoTracking().Where(p => p.Id == a.ProfissionalId).Select(p => p.Nome).FirstAsync(ct);
        var servicoIds = a.Itens.OrderBy(i => i.Ordem).Select(i => i.ServicoId).ToList();
        var nomes = await db.Servicos.AsNoTracking()
            .Where(s => servicoIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Nome, ct);
        var fuso = await FusoHorarioEmpresa.ObterAsync(db, a.EmpresaId, ct);
        return new AgendamentoPublicoDto(
            empresa?.Nome ?? "Lyra",
            empresa?.Endereco,
            empresa?.Telefone,
            cliente.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? cliente,
            profissional,
            servicoIds.Where(nomes.ContainsKey).Select(id => nomes[id]).ToList(),
            a.Inicio,
            a.Fim,
            fuso.Id,
            a.Status,
            PodeConfirmar(a));
    }
}
