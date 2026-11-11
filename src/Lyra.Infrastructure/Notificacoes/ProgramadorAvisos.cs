using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using Lyra.Application.Abstractions;
using Lyra.Application.Agendamentos;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Lyra.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lyra.Infrastructure.Notificacoes;

/// <summary>
/// Põe na fila o que depende do relógio: o lembrete ao cliente algumas horas antes e o resumo do dia
/// do profissional de manhã. Cada aviso reserva uma chave em <c>lyra.avisos_programados</c> na mesma
/// transação da notificação, então sai uma vez só.
/// </summary>
public sealed class ProgramadorAvisos(
    LyraDbContext db,
    FilaNotificacoes fila,
    IEnumerable<ICanalNotificacao> canais,
    IOptions<NotificacoesOptions> opcoes,
    IConfiguracaoEmpresaService configuracoes,
    SinalNotificacoes sinal,
    ILogger<ProgramadorAvisos> logger)
{
    /// <summary>Hora local (fuso da empresa) a partir da qual o resumo do dia sai.</summary>
    public const int HoraResumo = 7;
    /// <summary>Se a API ficou fora a manhã toda, o resumo do dia perdeu o sentido.</summary>
    private const int UltimaHoraResumo = 12;
    /// <summary>Mais que a maior antecedência de lembrete (72 h): depois disso a chave não protege mais nada.</summary>
    private static readonly TimeSpan GuardaChaves = TimeSpan.FromDays(4);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] Ativos = [StatusAgendamento.Agendado, StatusAgendamento.Confirmado];

    /// <param name="agora">Instante (UTC) que decide o que já está na hora; nos testes, simulado.</param>
    public async Task<int> ProgramarAsync(DateTime agora, CancellationToken ct)
    {
        var ligados = canais
            .Where(c => opcoes.Value.Canais.Contains(c.Nome, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (ligados.Count == 0) return 0;

        var programados = await LembretesAsync(ligados, agora, ct) + await ResumosAsync(ligados, agora, ct);
        var vencidas = DateTime.UtcNow - GuardaChaves;
        await db.AvisosProgramados.Where(a => a.DataCriacao < vencidas).ExecuteDeleteAsync(ct);
        if (programados > 0) sinal.Acordar();
        return programados;
    }

    private async Task<int> LembretesAsync(List<ICanalNotificacao> ligados, DateTime agora, CancellationToken ct)
    {
        var configs = (await db.EmpresaConfigs.AsNoTracking()
                .Where(c => c.LembretesHoras.Length > 0)
                .ToListAsync(ct))
            .Where(c => ligados.Any(l => !l.ParaEquipe
                && l.Aceita(EventosNotificacao.AgendamentoLembrete, FilaNotificacoes.OrigemSistema, c)))
            .ToDictionary(c => c.EmpresaId);
        if (configs.Count == 0) return 0;

        var empresas = configs.Keys.ToList();
        var limite = agora.AddHours(configs.Values.Max(c => c.LembretesHoras.Max()));
        var candidatos = await db.Agendamentos.AsNoTracking()
            .Where(a => empresas.Contains(a.EmpresaId)
                && Ativos.Contains(a.Status)
                && a.Inicio > agora && a.Inicio <= limite)
            .Select(a => new { a.Id, a.EmpresaId, a.Inicio, Alterado = a.DataAtualizacao ?? a.DataCriacao })
            .ToListAsync(ct);

        var programados = 0;
        foreach (var c in candidatos)
        {
            // Só o lembrete mais próximo que já venceu: com 24 h e 2 h, quem chega atrasado não recebe os dois.
            var vencidos = configs[c.EmpresaId].LembretesHoras.Where(h => c.Inicio.AddHours(-h) <= agora).ToList();
            if (vencidos.Count == 0) continue;
            var horas = vencidos.Min();
            // Marcado ou mexido já dentro da janela: o cliente acabou de receber o aviso do próprio agendamento.
            if (c.Alterado > c.Inicio.AddHours(-horas)) continue;

            var chave = string.Create(CultureInfo.InvariantCulture, $"lembrete:{c.Id}:{horas}:{c.Inicio:yyyyMMddHHmm}");
            var ok = await ReservarAsync(chave, c.EmpresaId, async () =>
            {
                var agendamento = await db.Agendamentos.Include(a => a.Itens).FirstAsync(a => a.Id == c.Id, ct);
                await fila.EnfileirarAsync(agendamento, EventosNotificacao.AgendamentoLembrete, ct,
                    origem: FilaNotificacoes.OrigemSistema, referencia: agora);
            }, ct);
            if (ok) programados++;
        }
        return programados;
    }

    private async Task<int> ResumosAsync(List<ICanalNotificacao> ligados, DateTime agora, CancellationToken ct)
    {
        var email = ligados.FirstOrDefault(c => c.Nome == CanalEmail.NomeCanal);
        if (email is null) return 0;
        var configs = await db.EmpresaConfigs.AsNoTracking().Where(c => c.ResumoDiario).ToListAsync(ct);

        var programados = 0;
        foreach (var config in configs)
        {
            var fuso = FusoHorarioEmpresa.Resolver(config.FusoHorario);
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(agora, DateTimeKind.Utc), fuso);
            if (local.Hour is < HoraResumo or >= UltimaHoraResumo) continue;

            var dia = local.Date;
            var inicioDia = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dia, DateTimeKind.Unspecified), fuso);
            var fimDia = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dia.AddDays(1), DateTimeKind.Unspecified), fuso);
            var agendamentos = await db.Agendamentos.AsNoTracking()
                .Where(a => a.EmpresaId == config.EmpresaId
                    && Ativos.Contains(a.Status)
                    && a.Inicio >= inicioDia && a.Inicio < fimDia
                    && a.Profissional.Ativo && a.Profissional.PessoaId != null)
                .OrderBy(a => a.Inicio)
                .Select(a => new
                {
                    a.ProfissionalId,
                    Profissional = a.Profissional.Nome,
                    PessoaId = a.Profissional.PessoaId!.Value,
                    a.Inicio,
                    a.Fim,
                    Cliente = a.Cliente.Nome,
                    a.Status,
                    a.Observacoes,
                    Servicos = a.Itens.OrderBy(i => i.Ordem).Select(i => i.Servico.Nome).ToList(),
                })
                .ToListAsync(ct);
            if (agendamentos.Count == 0) continue;

            var chaves = agendamentos.Select(a => a.ProfissionalId).Distinct()
                .ToDictionary(p => p, p => string.Create(CultureInfo.InvariantCulture, $"resumo:{p}:{dia:yyyy-MM-dd}"));
            var lista = chaves.Values.ToList();
            var feitas = await db.AvisosProgramados.AsNoTracking()
                .Where(a => lista.Contains(a.Chave)).Select(a => a.Chave).ToListAsync(ct);
            var pendentes = agendamentos.Where(a => !feitas.Contains(chaves[a.ProfissionalId])).ToList();
            if (pendentes.Count == 0) continue;

            var emails = await EmailsAsync(pendentes.Select(a => a.PessoaId).Distinct().ToArray(), ct);
            var termo = (await configuracoes.ObterAsync(config.EmpresaId, ct)).Terminologia.Agendamento;
            foreach (var grupo in pendentes.GroupBy(a => a.ProfissionalId))
            {
                var primeiro = grupo.First();
                if (!emails.TryGetValue(primeiro.PessoaId, out var destino)) continue;
                var dados = new DadosResumo
                {
                    Profissional = primeiro.Profissional,
                    Dia = dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    DiaLocal = dia.ToString("dddd, dd/MM", PtBr),
                    Singular = termo.Singular.ToLower(PtBr),
                    Plural = termo.Plural.ToLower(PtBr),
                    Itens = grupo.Select(a => new ItemResumo
                    {
                        Inicio = Local(a.Inicio, fuso),
                        Fim = Local(a.Fim, fuso),
                        Cliente = a.Cliente,
                        Servicos = a.Servicos,
                        Confirmado = a.Status == StatusAgendamento.Confirmado,
                        Observacoes = string.IsNullOrWhiteSpace(a.Observacoes) ? null : a.Observacoes.Trim(),
                    }).ToList(),
                };
                var ok = await ReservarAsync(chaves[grupo.Key], config.EmpresaId, () =>
                {
                    var criacao = DateTime.UtcNow;
                    db.Notificacoes.Add(new Notificacao
                    {
                        EmpresaId = config.EmpresaId,
                        Evento = EventosNotificacao.ResumoDia,
                        Canal = email.Nome,
                        Destino = destino,
                        Dados = JsonSerializer.Serialize(dados, Json),
                        Status = StatusNotificacao.Pendente,
                        ProximaTentativa = criacao,
                        DataCriacao = criacao,
                    });
                    return Task.CompletedTask;
                }, ct);
                if (ok) programados++;
            }
        }
        return programados;
    }

    /// <summary>Reserva a chave e enfileira na mesma transação. Chave já usada = o aviso já saiu.</summary>
    private async Task<bool> ReservarAsync(string chave, int empresaId, Func<Task> enfileirar, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var inseridas = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO lyra.avisos_programados (chave, empresa_id, data_criacao)
                VALUES ({chave}, {empresaId}, {DateTime.UtcNow})
                ON CONFLICT (chave) DO NOTHING
                """, ct);
            if (inseridas == 0) return false;
            await enfileirar();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Falha ao programar o aviso {Chave}", chave);
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private sealed record EmailPessoa(int PessoaId, string? Email);

    // O cadastro de pessoas é do Core: o e-mail de login da pessoa ou, sem ele, o do usuário ligado a ela.
    private const string SqlEmails = """
        SELECT p.id AS "PessoaId",
               COALESCE(NULLIF(btrim(p.email_login), ''),
                        (SELECT u.email FROM core.usuario_pessoa up JOIN core.usuarios u ON u.id = up.usuario_id
                          WHERE up.pessoa_id = p.id AND NULLIF(btrim(u.email), '') IS NOT NULL
                          ORDER BY u.id LIMIT 1)) AS "Email"
          FROM core.pessoas p
         WHERE p.id = ANY({0})
        """;

    private async Task<Dictionary<int, string>> EmailsAsync(int[] pessoas, CancellationToken ct)
    {
        try
        {
            var linhas = await db.Database.SqlQueryRaw<EmailPessoa>(SqlEmails, pessoas).ToListAsync(ct);
            return linhas
                .Where(l => l.Email is { } e && MailAddress.TryCreate(e.Trim(), out _))
                .ToDictionary(l => l.PessoaId, l => l.Email!.Trim());
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable
            or PostgresErrorCodes.InvalidSchemaName or PostgresErrorCodes.UndefinedColumn)
        {
            logger.LogWarning("Cadastro de pessoas do Core indisponível neste banco; resumo do dia não sai");
            return [];
        }
    }

    private static string Local(DateTime utc, TimeZoneInfo fuso) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), fuso).ToString("HH:mm", PtBr);
}
