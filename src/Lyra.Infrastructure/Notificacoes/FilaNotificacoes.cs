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
    public const string OrigemCliente = "cliente";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task EnfileirarAsync(
        Agendamento agendamento,
        string evento,
        CancellationToken ct,
        DateTime? inicioAnterior = null,
        string? motivo = null,
        string? origem = null)
    {
        var ligados = canais
            .Where(c => opcoes.Value.Canais.Contains(c.Nome, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (ligados.Count == 0) return;

        var config = await db.EmpresaConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmpresaId == agendamento.EmpresaId, ct);
        var ativos = ligados.Where(c => c.Aceita(evento, origem, config)).ToList();
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

        var dados = JsonSerializer.Serialize(new DadosNotificacao
        {
            Cliente = cliente.Nome,
            Profissional = profissional,
            Servicos = servicoIds.Where(nomes.ContainsKey).Select(id => nomes[id]).ToList(),
            Inicio = agendamento.Inicio,
            Fim = agendamento.Fim,
            InicioLocal = Local(agendamento.Inicio, fuso),
            InicioAnterior = inicioAnterior,
            InicioAnteriorLocal = inicioAnterior is DateTime ant ? Local(ant, fuso) : null,
            Fuso = fuso.Id,
            Motivo = motivo,
            Origem = origem,
            Cadeia = await CadeiaAsync(agendamento, ct),
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

    public static DadosNotificacao LerDados(string json) =>
        JsonSerializer.Deserialize<DadosNotificacao>(json, Json) ?? new DadosNotificacao();

    /// <summary>Primeiro agendamento da cadeia de remarcações: o calendário do cliente trata todos como o mesmo evento.</summary>
    private async Task<int> CadeiaAsync(Agendamento agendamento, CancellationToken ct)
    {
        var anterior = agendamento.RemarcadoDeId;
        var raiz = agendamento.Id;
        for (var passos = 0; anterior is int id && passos < 50; passos++)
        {
            raiz = id;
            anterior = await db.Agendamentos.AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => a.RemarcadoDeId)
                .FirstOrDefaultAsync(ct);
        }
        return raiz;
    }

    private static string Local(DateTime utc, TimeZoneInfo fuso) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), fuso)
            .ToString("dd/MM/yyyy HH:mm", PtBr);
}

/// <summary>O que a mensagem precisa saber do agendamento, congelado no momento do evento.</summary>
public sealed class DadosNotificacao
{
    public string Cliente { get; init; } = "";
    public string Profissional { get; init; } = "";
    public List<string> Servicos { get; init; } = [];
    public DateTime Inicio { get; init; }
    public DateTime Fim { get; init; }
    public string InicioLocal { get; init; } = "";
    public DateTime? InicioAnterior { get; init; }
    public string? InicioAnteriorLocal { get; init; }
    public string Fuso { get; init; } = "";
    public string? Motivo { get; init; }
    public string? Origem { get; init; }
    /// <summary>Id do primeiro agendamento da cadeia de remarcações (0 quando ainda não salvo).</summary>
    public int Cadeia { get; init; }
}
