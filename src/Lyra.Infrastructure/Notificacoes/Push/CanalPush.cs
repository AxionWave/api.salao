using System.Globalization;
using System.Net;
using System.Text.Json;
using Lyra.Application.Abstractions;
using Lyra.Application.Configuracao;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lyra.Infrastructure.Notificacoes.Push;

/// <summary>
/// Os mesmos avisos do sino, no celular ou no computador da equipe com o Lyra fechado (Web Push).
/// Vai para todos os aparelhos assinados da empresa, menos os de quem fez a mudança.
/// </summary>
public sealed class CanalPush(
    LyraDbContext db,
    ClienteWebPush cliente,
    IConfiguracaoEmpresaService configuracoes,
    ILogger<CanalPush> logger) : ICanalNotificacao
{
    public const string NomeCanal = "push";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public string Nome => NomeCanal;

    public bool ParaEquipe => true;

    public bool Aceita(string evento, string? origem, EmpresaConfig? config) =>
        evento != EventosNotificacao.AgendamentoLembrete && AvisosEmpresa.Ler(config?.Avisos).Equipe(evento);

    public string? Destino(Cliente cliente) => null;

    public async Task<string?> EnviarAsync(Notificacao n, CancellationToken ct)
    {
        var dados = FilaNotificacoes.LerDados(n.Dados);
        var aparelhos = await db.PushAssinaturas.AsNoTracking()
            .Where(a => a.EmpresaId == n.EmpresaId && (dados.AutorId == null || a.UsuarioId != dados.AutorId))
            .ToListAsync(ct);
        if (aparelhos.Count == 0) return null;

        var termos = (await configuracoes.ObterAsync(n.EmpresaId, ct)).Terminologia;
        var mensagem = JsonSerializer.SerializeToUtf8Bytes(new
        {
            titulo = Titulo(n.Evento, dados.Origem, termos),
            corpo = Corpo(dados),
            url = Url(dados.InicioLocal),
            tag = n.AgendamentoId is int ag ? $"lyra-agendamento-{ag}" : $"lyra-{n.Id}",
        }, Json);
        var topico = n.AgendamentoId is int id ? $"ag{id}" : null;

        int entregues = 0, transitorias = 0;
        var removidos = new List<long>();
        string? ultimoErro = null;
        foreach (var a in aparelhos)
        {
            try
            {
                var status = await cliente.EnviarAsync(a.Endpoint, a.P256dh, a.Auth, mensagem, topico, ct);
                switch ((int)status)
                {
                    case >= 200 and < 300:
                        entregues++;
                        break;
                    // A assinatura acabou (permissão retirada, app desinstalado) ou nunca valeu.
                    case 404 or 410 or 400 or 403:
                        removidos.Add(a.Id);
                        break;
                    default:
                        transitorias++;
                        ultimoErro = $"Serviço de push respondeu {(int)status}";
                        break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                transitorias++;
                ultimoErro = ex.Message;
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or System.Security.Cryptography.CryptographicException)
            {
                logger.LogWarning(ex, "Assinatura de push inválida ({Id}); removida", a.Id);
                removidos.Add(a.Id);
            }
        }

        var agora = DateTime.UtcNow;
        if (removidos.Count > 0)
            await db.PushAssinaturas.Where(a => removidos.Contains(a.Id)).ExecuteDeleteAsync(ct);
        if (entregues > 0)
        {
            var ids = aparelhos.Select(a => a.Id).Except(removidos).ToList();
            await db.PushAssinaturas.Where(a => ids.Contains(a.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.UltimoEnvio, agora), ct);
        }
        // Nenhum aparelho recebeu e algum pode receber depois: o worker tenta de novo (quem recebeu não existe).
        if (entregues == 0 && transitorias > 0) throw new HttpRequestException(ultimoErro);
        return null;
    }

    public static string Titulo(string evento, string? origem, TerminologiaDto termos)
    {
        var termo = termos.Agendamento;
        var feminino = termo.Genero == "f";
        return evento switch
        {
            EventosNotificacao.AgendamentoCriado => $"{(feminino ? "Nova" : "Novo")} {termo.Singular.ToLower(PtBr)}"
                + (origem == FilaNotificacoes.OrigemCliente ? " online" : ""),
            EventosNotificacao.AgendamentoRemarcado => $"{termo.Singular} {(feminino ? "alterada" : "alterado")}",
            EventosNotificacao.AgendamentoCancelado => $"{termo.Singular} {(feminino ? "cancelada" : "cancelado")}",
            EventosNotificacao.AgendamentoConfirmado => origem == FilaNotificacoes.OrigemCliente
                ? $"{termos.Cliente.Singular} confirmou presença"
                : "Presença confirmada",
            _ => "Aviso da agenda",
        };
    }

    /// <summary>"Maria · Ana · 24/11 às 15:00".</summary>
    public static string Corpo(DadosNotificacao d)
    {
        var quando = DateTime.TryParseExact(d.InicioLocal, "dd/MM/yyyy HH:mm", PtBr, DateTimeStyles.None, out var dt)
            ? dt.ToString("dd/MM 'às' HH:mm", PtBr)
            : d.InicioLocal;
        return string.Join(" · ", new[] { d.Cliente, d.Profissional, quando }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>Abre a Agenda no dia do agendamento (relativo: o service worker resolve no endereço do Lyra).</summary>
    public static string Url(string inicioLocal) =>
        DateTime.TryParseExact(inicioLocal, "dd/MM/yyyy HH:mm", PtBr, DateTimeStyles.None, out var dt)
            ? $"agenda?data={dt:yyyy-MM-dd}&visao=dia"
            : "agenda";
}
