using System.Globalization;
using Lyra.Application.Configuracao;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Lyra.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Notificacoes;

public sealed class AvisosService(LyraDbContext db, LeitorEmpresa empresas) : IAvisosService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<AvisosResponse> ObterAsync(int empresaId, CancellationToken ct)
    {
        var config = await db.EmpresaConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.EmpresaId == empresaId, ct);
        return Resposta(config);
    }

    public async Task<AvisosResponse> SalvarAsync(int empresaId, AvisosDto pedido, CancellationToken ct)
    {
        var lembretes = ConfiguracaoEmpresaService.Lembretes(pedido.LembretesHoras ?? []);
        var eventos = new Dictionary<string, AvisosEmpresa.AvisoEvento>();
        foreach (var e in pedido.Eventos ?? [])
        {
            var modelo = ModelosAviso.Obter(e.Evento) ?? throw new ArgumentException($"Evento desconhecido: {e.Evento}.");
            if (eventos.ContainsKey(modelo.Evento)) throw new ArgumentException($"Evento repetido: {modelo.Nome}.");
            eventos[modelo.Evento] = new AvisosEmpresa.AvisoEvento
            {
                Cliente = e.Cliente,
                Equipe = modelo.Equipe && e.Equipe,
                Assunto = ModelosAviso.Normalizar(e.Assunto, modelo.Assunto, ModelosAviso.MaxAssunto, $"{modelo.Nome} – assunto", umaLinha: true),
                Mensagem = ModelosAviso.Normalizar(e.Mensagem, modelo.Mensagem, ModelosAviso.MaxMensagem, $"{modelo.Nome} – mensagem"),
            };
        }
        var avisos = new AvisosEmpresa
        {
            Eventos = eventos,
            Recado = ModelosAviso.Normalizar(pedido.Recado, null, ModelosAviso.MaxRecado, "Recado"),
        };

        var config = await db.EmpresaConfigs.FirstOrDefaultAsync(x => x.EmpresaId == empresaId, ct);
        if (config is null)
        {
            config = new EmpresaConfig { EmpresaId = empresaId, DataCriacao = DateTime.UtcNow };
            db.EmpresaConfigs.Add(config);
        }
        else
        {
            config.DataAtualizacao = DateTime.UtcNow;
        }
        config.EnviarEmails = pedido.EnviarEmails;
        config.LembretesHoras = lembretes;
        config.ResumoDiario = pedido.ResumoDiario;
        config.Avisos = avisos.Serializar();
        await db.SaveChangesAsync(ct);
        return Resposta(config);
    }

    public async Task<PreviaAvisoResponse> PreviaAsync(int empresaId, PreviaAvisoRequest pedido, CancellationToken ct)
    {
        var modelo = ModelosAviso.Obter(pedido.Evento) ?? throw new ArgumentException($"Evento desconhecido: {pedido.Evento}.");
        var textos = new TextosAviso(
            ModelosAviso.Normalizar(pedido.Assunto, null, ModelosAviso.MaxAssunto, "Assunto", umaLinha: true),
            ModelosAviso.Normalizar(pedido.Mensagem, null, ModelosAviso.MaxMensagem, "Mensagem"),
            ModelosAviso.Normalizar(pedido.Recado, null, ModelosAviso.MaxRecado, "Recado"));

        var empresa = await empresas.ObterAsync(empresaId, ct);
        var dados = await Exemplo(empresaId, modelo.Evento, ct);
        // Sem link de verdade: a prévia não confirma nada nem descadastra ninguém.
        var email = ModelosEmail.Montar(modelo.Evento, 0, dados, empresa, "#", textos);
        return new PreviaAvisoResponse(
            email.Assunto,
            email.Html.Replace(ModelosEmail.MarcadorDescadastro, "#"),
            email.Texto.Replace(ModelosEmail.MarcadorDescadastro, "(link para sair da lista)"));
    }

    /// <summary>Um horário amanhã às 15h com os primeiros serviços e profissional da empresa.</summary>
    private async Task<DadosNotificacao> Exemplo(int empresaId, string evento, CancellationToken ct)
    {
        var fuso = await FusoHorarioEmpresa.ObterAsync(db, empresaId, ct);
        var profissional = await db.Profissionais.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.Ativo)
            .OrderBy(p => p.Nome)
            .Select(p => p.Nome)
            .FirstOrDefaultAsync(ct) ?? "Ana";
        var servicos = await db.Servicos.AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Ativo)
            .OrderBy(s => s.Nome)
            .Take(2)
            .Select(s => new ItemNotificacao { Nome = s.Nome, DuracaoMinutos = s.DuracaoMinutos, Preco = s.Preco })
            .ToListAsync(ct);
        if (servicos.Count == 0) servicos = [new ItemNotificacao { Nome = "Corte", DuracaoMinutos = 45, Preco = 80 }];

        var agora = DateTime.UtcNow;
        var amanha = TimeZoneInfo.ConvertTimeFromUtc(agora, fuso).Date.AddDays(1);
        var inicioLocal = amanha.AddHours(15);
        var anteriorLocal = amanha.AddHours(10);
        var inicio = TimeZoneInfo.ConvertTimeToUtc(inicioLocal, fuso);
        return new DadosNotificacao
        {
            Cliente = "Maria Silva",
            Profissional = profissional,
            Servicos = servicos.Select(s => s.Nome).ToList(),
            Itens = servicos,
            Inicio = inicio,
            Fim = inicio.AddMinutes(Math.Max(15, servicos.Sum(s => s.DuracaoMinutos))),
            InicioLocal = inicioLocal.ToString("dd/MM/yyyy HH:mm", PtBr),
            InicioAnteriorLocal = evento == EventosNotificacao.AgendamentoRemarcado ? anteriorLocal.ToString("dd/MM/yyyy HH:mm", PtBr) : null,
            InicioAnterior = evento == EventosNotificacao.AgendamentoRemarcado ? TimeZoneInfo.ConvertTimeToUtc(anteriorLocal, fuso) : null,
            Fuso = fuso.Id,
            Referencia = agora,
        };
    }

    private static AvisosResponse Resposta(EmpresaConfig? config)
    {
        var avisos = AvisosEmpresa.Ler(config?.Avisos);
        var eventos = ModelosAviso.Todos.Select(m =>
        {
            var e = avisos.Evento(m.Evento);
            return new AvisoEventoDto(m.Evento, e.Cliente, m.Equipe && e.Equipe, e.Assunto, e.Mensagem);
        }).ToList();
        return new AvisosResponse(
            new AvisosDto(config?.EnviarEmails ?? false, config?.LembretesHoras ?? [24], config?.ResumoDiario ?? false, eventos, avisos.Recado),
            ModelosAviso.Todos,
            ModelosAviso.Variaveis);
    }
}
