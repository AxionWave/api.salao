using System.Globalization;
using System.Text.Json;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Lyra.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>
/// Põe as notificações de um evento no contexto, sem salvar: quem chama salva junto com o agendamento,
/// e assim a mensagem só existe se o agendamento existir.
/// </summary>
public sealed class FilaNotificacoes(
    LyraDbContext db,
    IEnumerable<ICanalNotificacao> canais,
    IOptions<NotificacoesOptions> opcoes)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task EnfileirarAsync(
        Agendamento agendamento,
        string evento,
        CancellationToken ct,
        DateTime? inicioAnterior = null,
        string? motivo = null)
    {
        var ativos = canais
            .Where(c => opcoes.Value.Canais.Contains(c.Nome, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (ativos.Count == 0) return;

        if (agendamento.Itens.Count == 0 && db.Entry(agendamento).State != EntityState.Added)
        {
            await db.Entry(agendamento).Collection(a => a.Itens).LoadAsync(ct);
        }
        var cliente = await db.Clientes.AsNoTracking().FirstAsync(c => c.Id == agendamento.ClienteId, ct);
        var profissional = await db.Profissionais.AsNoTracking()
            .Where(p => p.Id == agendamento.ProfissionalId)
            .Select(p => p.Nome)
            .FirstAsync(ct);
        var servicoIds = agendamento.Itens.OrderBy(i => i.Ordem).Select(i => i.ServicoId).ToList();
        var nomes = await db.Servicos.AsNoTracking()
            .Where(s => servicoIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Nome, ct);
        var fuso = await FusoHorarioEmpresa.ObterAsync(db, agendamento.EmpresaId, ct);

        var dados = JsonSerializer.Serialize(new
        {
            cliente = cliente.Nome,
            profissional,
            servicos = servicoIds.Where(nomes.ContainsKey).Select(id => nomes[id]).ToList(),
            inicio = agendamento.Inicio,
            inicioLocal = Local(agendamento.Inicio, fuso),
            inicioAnterior,
            inicioAnteriorLocal = inicioAnterior is DateTime ant ? Local(ant, fuso) : null,
            fuso = fuso.Id,
            motivo,
        }, Json);

        var agora = DateTime.UtcNow;
        foreach (var canal in ativos)
        {
            var destino = canal.Destino(cliente);
            if (destino is null) continue;
            db.Notificacoes.Add(new Notificacao
            {
                EmpresaId = agendamento.EmpresaId,
                Evento = evento,
                Canal = canal.Nome,
                Destino = destino,
                ClienteId = cliente.Id,
                Agendamento = agendamento,
                Dados = dados,
                Status = StatusNotificacao.Pendente,
                ProximaTentativa = agora,
                DataCriacao = agora,
            });
        }
    }

    private static string Local(DateTime utc, TimeZoneInfo fuso) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), fuso)
            .ToString("dd/MM/yyyy HH:mm", PtBr);
}
