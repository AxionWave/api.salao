using Lyra.Application.Abstractions;
using Lyra.Application.Agendamentos;
using Lyra.Application.Notificacoes;
using Lyra.Infrastructure.AoVivo;
using Lyra.Infrastructure.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lyra.Infrastructure.Services;

public sealed class AgendamentoService(LyraDbContext db, FilaNotificacoes fila, AvisosAoVivo aoVivo) : IAgendamentoService
{
    private const int MaxIntervaloApos = ServicoService.MaxIntervaloAposMinutos;
    public const int MaxDiasListagem = 62;
    private const int ToleranciaPassadoMinutos = 5;
    private const int InicioAntecipadoMaxMinutos = 60;

    public async Task<ListaAgendamentosResponse> ListarAsync(
        int empresaId,
        DateTime de,
        DateTime ate,
        int? profissionalId,
        string? status,
        CancellationToken ct)
    {
        var inicio = DatasUtc.Normalizar(de);
        var fim = DatasUtc.Normalizar(ate);
        if (fim <= inicio)
        {
            throw new ArgumentException("O fim do período deve ser depois do início.");
        }
        if ((fim - inicio).TotalDays > MaxDiasListagem)
        {
            throw new ArgumentException($"O período pode ter no máximo {MaxDiasListagem} dias.");
        }

        var q = QueryCompleto(empresaId).Where(a => a.Inicio < fim && a.Fim > inicio);

        if (profissionalId is int pid)
        {
            q = q.Where(a => a.ProfissionalId == pid);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!StatusAgendamento.Valido(status))
            {
                throw new ArgumentException("Status inválido.");
            }
            var st = StatusAgendamento.Normalizar(status);
            q = q.Where(a => a.Status == st);
        }

        var itens = await q.OrderBy(a => a.Inicio).ThenBy(a => a.ProfissionalId).ToListAsync(ct);
        return new ListaAgendamentosResponse(itens.Select(Map).ToList());
    }

    public async Task<OpcoesAgendaResponse> OpcoesAsync(int empresaId, CancellationToken ct)
    {
        var profissionais = await db.Profissionais.AsNoTracking()
            .Include(p => p.Disponibilidades)
            .Where(p => p.EmpresaId == empresaId
                && (p.Ativo || db.Agendamentos.Any(a =>
                    a.ProfissionalId == p.Id && a.Status != StatusAgendamento.Cancelado)))
            .OrderBy(p => p.Nome)
            .ToListAsync(ct);

        var servicos = await db.Servicos.AsNoTracking()
            .Include(s => s.Profissionais)
            .Where(s => s.EmpresaId == empresaId && s.Ativo)
            .OrderBy(s => s.Nome)
            .ToListAsync(ct);

        var fuso = await FusoHorarioEmpresa.ObterAsync(db, empresaId, ct);

        return new OpcoesAgendaResponse(
            profissionais.Select(p => new ProfissionalAgendaDto(
                p.Id,
                p.Nome,
                p.Cor,
                p.Disponibilidades
                    .OrderBy(d => d.DiaSemana)
                    .ThenBy(d => d.Inicio)
                    .Select(d => new IntervaloAgendaDto(d.DiaSemana, HhMm(d.Inicio), HhMm(d.Fim)))
                    .ToList(),
                p.Ativo)).ToList(),
            servicos.Select(s => new ServicoAgendaDto(
                s.Id,
                s.Nome,
                s.DuracaoMinutos,
                s.Preco,
                s.IntervaloAposMinutos,
                s.Profissionais.Select(v => v.ProfissionalId).Order().ToList())).ToList(),
            fuso.Id);
    }

    public async Task<AgendamentoDto?> ObterAsync(int empresaId, int id, CancellationToken ct)
    {
        var a = await QueryCompleto(empresaId).FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : Map(a);
    }

    public async Task<AgendamentoDto> CriarAsync(
        int empresaId, SalvarAgendamentoRequest request, CancellationToken ct, string? origem = null, int? contaClienteId = null,
        bool confirmado = false)
    {
        var v = await ValidarAsync(empresaId, request, atual: null, ct);
        await using var tx = await TravarAgendaAsync(empresaId, v.Profissional.Id, ct);
        var entidade = await NovoAgendamentoAsync(empresaId, v, ignorarId: null, remarcadoDeId: null, ct);
        entidade.ContaClienteId = contaClienteId;
        if (confirmado) entidade.Status = StatusAgendamento.Confirmado;
        await fila.EnfileirarAsync(entidade, EventosNotificacao.AgendamentoCriado, ct, origem: origem);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await aoVivo.AgendaAlteradaAsync(empresaId, entidade.ProfissionalId);
        return (await ObterAsync(empresaId, entidade.Id, ct))!;
    }

    public async Task<AgendamentoDto> RemarcarAsync(
        int empresaId, int id, SalvarAgendamentoRequest request, CancellationToken ct, string? origem = null,
        bool confirmado = false)
    {
        var original = await db.Agendamentos.FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Agendamento não encontrado.");
        if (original.Status is StatusAgendamento.EmAtendimento or StatusAgendamento.Concluido)
        {
            throw new InvalidOperationException("Atendimento iniciado ou concluído não pode ser remarcado.");
        }
        var jaRemarcado = await db.Agendamentos.AnyAsync(
            x => x.RemarcadoDeId == id && x.Status != StatusAgendamento.Cancelado, ct);
        if (jaRemarcado)
        {
            throw new InvalidOperationException("Este agendamento já foi remarcado. Altere o novo horário.");
        }

        var emAberto = !StatusAgendamento.Terminal(original.Status);
        var v = await ValidarAsync(empresaId, request with { ClienteId = original.ClienteId }, atual: null, ct);
        await using var tx = await TravarAgendaAsync(empresaId, v.Profissional.Id, ct);
        var novo = await NovoAgendamentoAsync(empresaId, v, emAberto ? original.Id : null, original.Id, ct);
        novo.ContaClienteId = original.ContaClienteId;
        if (confirmado) novo.Status = StatusAgendamento.Confirmado;
        if (emAberto)
        {
            original.Status = StatusAgendamento.Cancelado;
            original.MotivoCancelamento = "Remarcado";
            original.DataAtualizacao = DateTime.UtcNow;
        }
        await fila.EnfileirarAsync(novo, EventosNotificacao.AgendamentoRemarcado, ct, inicioAnterior: original.Inicio, origem: origem);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await aoVivo.AgendaAlteradaAsync(empresaId);
        return (await ObterAsync(empresaId, novo.Id, ct))!;
    }

    /// <summary>Confere as regras de um horário novo e adiciona ao contexto (sem salvar). Exige a trava da agenda.</summary>
    private async Task<Agendamento> NovoAgendamentoAsync(
        int empresaId,
        Validado v,
        int? ignorarId,
        int? remarcadoDeId,
        CancellationToken ct)
    {
        GarantirNaoPassado(v.Inicio);
        GarantirAtendeServicos(v.Profissional, v.Servicos);
        await GarantirExpedienteAsync(empresaId, v.Profissional, v.Inicio, v.Fim, ct);
        await GarantirSemBloqueioAsync(empresaId, v.Profissional.Id, v.Inicio, v.Fim, ct);
        await GarantirLivreAsync(empresaId, v.Profissional, v.Inicio, v.Fim, v.IntervaloApos, ignorarId, ct);

        var entidade = new Agendamento
        {
            EmpresaId = empresaId,
            ClienteId = v.Cliente.Id,
            ProfissionalId = v.Profissional.Id,
            Inicio = v.Inicio,
            Fim = v.Fim,
            IntervaloAposMinutos = v.IntervaloApos,
            Status = StatusAgendamento.Agendado,
            Observacoes = v.Obs,
            RemarcadoDeId = remarcadoDeId,
            DataCriacao = DateTime.UtcNow,
            Itens = MontarItens(v.Servicos),
        };
        db.Agendamentos.Add(entidade);
        return entidade;
    }

    public async Task<AgendamentoDto> AtualizarAsync(int empresaId, int id, SalvarAgendamentoRequest request, CancellationToken ct)
    {
        var entidade = await db.Agendamentos
            .Include(x => x.Itens)
            .FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Agendamento não encontrado.");
        if (StatusAgendamento.Terminal(entidade.Status))
        {
            throw new InvalidOperationException("Não é possível alterar um agendamento encerrado.");
        }

        var v = await ValidarAsync(empresaId, request, entidade, ct);
        var mudouProfissional = entidade.ProfissionalId != v.Profissional.Id;
        var mudouServicos = !v.MesmosServicos;
        var mudouHorario = mudouProfissional || entidade.Inicio != v.Inicio || entidade.Fim != v.Fim;
        if (entidade.Inicio != v.Inicio)
        {
            GarantirNaoPassado(v.Inicio);
        }
        if (mudouProfissional || mudouServicos)
        {
            GarantirAtendeServicos(v.Profissional, v.Servicos);
        }
        await using var tx = mudouHorario || mudouServicos
            ? await TravarAgendaAsync(empresaId, v.Profissional.Id, ct)
            : null;
        if (mudouHorario)
        {
            await GarantirExpedienteAsync(empresaId, v.Profissional, v.Inicio, v.Fim, ct);
            await GarantirSemBloqueioAsync(empresaId, v.Profissional.Id, v.Inicio, v.Fim, ct);
        }
        if (mudouHorario || mudouServicos)
        {
            await GarantirLivreAsync(empresaId, v.Profissional, v.Inicio, v.Fim, v.IntervaloApos, id, ct);
        }

        var inicioAnterior = entidade.Inicio;
        entidade.ClienteId = v.Cliente.Id;
        entidade.ProfissionalId = v.Profissional.Id;
        entidade.Inicio = v.Inicio;
        entidade.Fim = v.Fim;
        entidade.IntervaloAposMinutos = v.IntervaloApos;
        entidade.Observacoes = v.Obs;
        entidade.DataAtualizacao = DateTime.UtcNow;
        if (mudouServicos)
        {
            entidade.Itens.Clear();
            foreach (var item in MontarItens(v.Servicos))
            {
                entidade.Itens.Add(item);
            }
        }
        if (mudouProfissional || inicioAnterior != v.Inicio)
        {
            await fila.EnfileirarAsync(entidade, EventosNotificacao.AgendamentoRemarcado, ct, inicioAnterior: inicioAnterior);
        }
        await db.SaveChangesAsync(ct);
        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }
        await aoVivo.AgendaAlteradaAsync(empresaId);
        return (await ObterAsync(empresaId, id, ct))!;
    }

    public async Task<AgendamentoDto> AlterarStatusAsync(
        int empresaId, int id, string status, string? motivo, CancellationToken ct, string? origem = null)
    {
        motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        if (motivo is { Length: > 200 })
        {
            throw new ArgumentException("O motivo pode ter no máximo 200 caracteres.");
        }
        if (!StatusAgendamento.Valido(status))
        {
            throw new ArgumentException("Status inválido.");
        }
        var novo = StatusAgendamento.Normalizar(status);
        var entidade = await db.Agendamentos.FirstOrDefaultAsync(x => x.Id == id && x.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Agendamento não encontrado.");
        if (!StatusAgendamento.PodeTransitar(entidade.Status, novo))
        {
            throw new InvalidOperationException("Esta transição de status não é permitida.");
        }
        if (novo != entidade.Status)
        {
            var agora = DateTime.UtcNow;
            if ((novo is StatusAgendamento.Concluido or StatusAgendamento.NoShow) && entidade.Inicio > agora)
            {
                throw new InvalidOperationException("Só dá para concluir ou marcar falta depois do horário de início.");
            }
            if (novo == StatusAgendamento.EmAtendimento && entidade.Inicio > agora.AddMinutes(InicioAntecipadoMaxMinutos))
            {
                throw new InvalidOperationException("Só dá para iniciar o atendimento a partir de 1 hora antes do horário marcado.");
            }
        }
        if (novo == StatusAgendamento.Cancelado && entidade.Status != novo)
        {
            entidade.MotivoCancelamento = motivo;
        }
        var mudou = entidade.Status != novo;
        entidade.Status = novo;
        entidade.DataAtualizacao = DateTime.UtcNow;
        if (mudou && novo == StatusAgendamento.Cancelado)
        {
            await fila.EnfileirarAsync(entidade, EventosNotificacao.AgendamentoCancelado, ct, motivo: motivo, origem: origem);
        }
        else if (mudou && novo == StatusAgendamento.Confirmado)
        {
            await fila.EnfileirarAsync(entidade, EventosNotificacao.AgendamentoConfirmado, ct);
        }
        await db.SaveChangesAsync(ct);
        if (mudou)
        {
            await aoVivo.AgendaAlteradaAsync(empresaId, entidade.ProfissionalId);
        }
        return (await ObterAsync(empresaId, id, ct))!;
    }

    public async Task<HorariosLivresResponse> HorariosLivresAsync(
        int empresaId,
        HorariosLivresRequest request,
        CancellationToken ct)
    {
        if (request.PassoMinutos is < 5 or > 60)
        {
            throw new ArgumentException("O passo deve ser entre 5 e 60 minutos.");
        }

        var servicos = await CarregarServicosAsync(
            empresaId, request.ServicoIds, ct, permitirInativos: request.IgnorarAgendamentoId is not null);
        var duracao = servicos.Sum(s => s.DuracaoMinutos);
        var intervaloApos = servicos.Max(s => s.IntervaloAposMinutos);
        var fuso = await FusoHorarioEmpresa.ObterAsync(db, empresaId, ct);

        var qProf = db.Profissionais.AsNoTracking()
            .Include(p => p.Disponibilidades)
            .Where(p => p.EmpresaId == empresaId && p.Ativo);
        if (request.ProfissionalId is int pid)
        {
            qProf = qProf.Where(p => p.Id == pid);
        }
        var profissionais = (await qProf.OrderBy(p => p.Nome).ToListAsync(ct))
            .Where(p => servicos.All(s => Atende(s, p.Id)))
            .ToList();

        var inicioDia = ParaUtc(request.Data.ToDateTime(TimeOnly.MinValue), fuso);
        var fimDia = ParaUtc(request.Data.AddDays(1).ToDateTime(TimeOnly.MinValue), fuso);
        var profIds = profissionais.Select(p => p.Id).ToList();
        var ignorarId = request.IgnorarAgendamentoId ?? 0;
        var desde = inicioDia.AddMinutes(-MaxIntervaloApos);
        var ate = fimDia.AddMinutes(duracao + intervaloApos);

        var ocupados = await db.Agendamentos.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId
                && profIds.Contains(a.ProfissionalId)
                && a.Status != StatusAgendamento.Cancelado
                && a.Id != ignorarId
                && a.Inicio < ate
                && a.Fim > desde)
            .Select(a => new Ocupacao(a.ProfissionalId, a.Inicio, a.Fim, a.IntervaloAposMinutos))
            .ToListAsync(ct);
        var bloqueios = await db.Bloqueios.AsNoTracking()
            .Where(b => b.EmpresaId == empresaId
                && (b.ProfissionalId == null || profIds.Contains(b.ProfissionalId.Value))
                && b.Inicio < ate
                && b.Fim > inicioDia)
            .Select(b => new { b.ProfissionalId, b.Inicio, b.Fim })
            .ToListAsync(ct);

        var agora = DateTime.UtcNow;
        var diaSemana = (int)request.Data.DayOfWeek;
        var resultado = new List<HorariosLivresProfissionalDto>(profissionais.Count);
        foreach (var p in profissionais)
        {
            var horarios = new List<HorarioLivreDto>();
            var faixas = p.Disponibilidades.Where(d => d.DiaSemana == diaSemana).OrderBy(d => d.Inicio);
            foreach (var faixa in faixas)
            {
                var faixaFim = (int)faixa.Fim.ToTimeSpan().TotalMinutes;
                for (var m = (int)faixa.Inicio.ToTimeSpan().TotalMinutes; m + duracao <= faixaFim; m += request.PassoMinutos)
                {
                    var local = request.Data.ToDateTime(TimeOnly.MinValue).AddMinutes(m);
                    if (fuso.IsInvalidTime(local))
                    {
                        continue;
                    }
                    var inicio = ParaUtc(local, fuso);
                    var fim = inicio.AddMinutes(duracao);
                    if (inicio <= agora)
                    {
                        continue;
                    }
                    if (bloqueios.Any(b => (b.ProfissionalId == null || b.ProfissionalId == p.Id) && b.Inicio < fim && b.Fim > inicio))
                    {
                        continue;
                    }
                    if (ocupados.Any(o => o.ProfissionalId == p.Id && Conflita(o, inicio, fim, intervaloApos)))
                    {
                        continue;
                    }
                    horarios.Add(new HorarioLivreDto(inicio, local.ToString("HH\\:mm")));
                }
            }
            resultado.Add(new HorariosLivresProfissionalDto(p.Id, p.Nome, horarios));
        }

        return new HorariosLivresResponse(request.Data, duracao, fuso.Id, resultado);
    }

    private IQueryable<Agendamento> QueryCompleto(int empresaId) =>
        db.Agendamentos.AsNoTracking()
            .Include(a => a.Cliente)
            .Include(a => a.Profissional)
            .Include(a => a.Itens).ThenInclude(i => i.Servico)
            .Where(a => a.EmpresaId == empresaId);

    private sealed record Validado(
        Cliente Cliente,
        IReadOnlyList<Servico> Servicos,
        Profissional Profissional,
        DateTime Inicio,
        DateTime Fim,
        int IntervaloApos,
        string? Obs,
        bool MesmosServicos);

    private sealed record Ocupacao(int ProfissionalId, DateTime Inicio, DateTime Fim, int IntervaloAposMinutos);

    /// <param name="atual">
    /// Na edição, o agendamento gravado: o que não foi trocado pode estar inativo, e se os serviços
    /// são os mesmos valem a duração, o preço e o preparo gravados, não os do catálogo atual.
    /// </param>
    private async Task<Validado> ValidarAsync(
        int empresaId,
        SalvarAgendamentoRequest request,
        Agendamento? atual,
        CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClienteId && c.EmpresaId == empresaId, ct)
            ?? throw new ArgumentException("Cliente não encontrado nesta empresa.");
        if (!cliente.Ativo && atual?.ClienteId != cliente.Id)
        {
            throw new ArgumentException("Cliente inativo.");
        }

        var itensAtuais = atual?.Itens.OrderBy(i => i.Ordem).ThenBy(i => i.Id).ToList();
        var mesmosServicos = itensAtuais is not null
            && itensAtuais.Select(i => i.ServicoId).SequenceEqual(request.ServicoIds ?? []);
        var servicos = await CarregarServicosAsync(empresaId, request.ServicoIds, ct, permitirInativos: mesmosServicos);

        var profissional = await db.Profissionais.AsNoTracking()
            .Include(p => p.Disponibilidades)
            .FirstOrDefaultAsync(p => p.Id == request.ProfissionalId && p.EmpresaId == empresaId, ct)
            ?? throw new ArgumentException("Profissional não encontrado nesta empresa.");
        if (!profissional.Ativo && atual?.ProfissionalId != profissional.Id)
        {
            throw new ArgumentException("Profissional inativo.");
        }

        var duracao = mesmosServicos ? itensAtuais!.Sum(i => i.DuracaoMinutos) : servicos.Sum(s => s.DuracaoMinutos);
        var intervaloApos = mesmosServicos ? atual!.IntervaloAposMinutos : servicos.Max(s => s.IntervaloAposMinutos);
        var inicio = DatasUtc.Normalizar(request.Inicio);
        var fim = inicio.AddMinutes(duracao);
        if (fim <= inicio)
        {
            throw new ArgumentException("Horário inválido.");
        }

        var obs = string.IsNullOrWhiteSpace(request.Observacoes) ? null : request.Observacoes.Trim();
        if (obs is { Length: > 1000 })
        {
            throw new ArgumentException("Observações devem ter no máximo 1000 caracteres.");
        }

        return new Validado(cliente, servicos, profissional, inicio, fim, intervaloApos, obs, mesmosServicos);
    }

    private static void GarantirNaoPassado(DateTime inicio)
    {
        if (inicio < DateTime.UtcNow.AddMinutes(-ToleranciaPassadoMinutos))
        {
            throw new ArgumentException("Este horário já passou. Escolha um horário a partir de agora.");
        }
    }

    /// <summary>Serviços na ordem pedida (repetições permitidas), todos ativos e da empresa.</summary>
    private async Task<IReadOnlyList<Servico>> CarregarServicosAsync(
        int empresaId,
        IReadOnlyList<int>? servicoIds,
        CancellationToken ct,
        bool permitirInativos = false)
    {
        var ids = servicoIds ?? [];
        if (ids.Count == 0)
        {
            throw new ArgumentException("Informe ao menos um serviço.");
        }
        if (ids.Count > 20)
        {
            throw new ArgumentException("No máximo 20 serviços por horário.");
        }

        var encontrados = await db.Servicos.AsNoTracking()
            .Include(s => s.Profissionais)
            .Where(s => s.EmpresaId == empresaId && ids.Contains(s.Id))
            .ToListAsync(ct);
        var porId = encontrados.ToDictionary(s => s.Id);
        var servicos = new List<Servico>(ids.Count);
        foreach (var sid in ids)
        {
            if (!porId.TryGetValue(sid, out var servico))
            {
                throw new ArgumentException("Serviço não encontrado nesta empresa.");
            }
            if (!servico.Ativo && !permitirInativos)
            {
                throw new ArgumentException($"O serviço «{servico.Nome}» está inativo.");
            }
            if (servico.DuracaoMinutos < 1)
            {
                throw new ArgumentException($"Serviço «{servico.Nome}» com duração inválida.");
            }
            servicos.Add(servico);
        }
        return servicos;
    }

    private static bool Atende(Servico servico, int profissionalId) =>
        servico.Profissionais.Count == 0 || servico.Profissionais.Any(v => v.ProfissionalId == profissionalId);

    private static void GarantirAtendeServicos(Profissional profissional, IReadOnlyList<Servico> servicos)
    {
        var naoAtende = servicos.FirstOrDefault(s => !Atende(s, profissional.Id));
        if (naoAtende is not null)
        {
            throw new InvalidOperationException($"{profissional.Nome} não atende «{naoAtende.Nome}».");
        }
    }

    /// <summary>O agendamento inteiro precisa caber numa faixa de expediente do dia, no fuso da empresa.</summary>
    private async Task GarantirExpedienteAsync(
        int empresaId,
        Profissional profissional,
        DateTime inicio,
        DateTime fim,
        CancellationToken ct)
    {
        var fuso = await FusoHorarioEmpresa.ObterAsync(db, empresaId, ct);
        var localInicio = TimeZoneInfo.ConvertTimeFromUtc(inicio, fuso);
        var localFim = TimeZoneInfo.ConvertTimeFromUtc(fim, fuso);
        var minutoInicio = localInicio.TimeOfDay.TotalMinutes;
        var minutoFim = (localFim - localInicio.Date).TotalMinutes;
        if (minutoFim > 24 * 60)
        {
            throw new InvalidOperationException("O atendimento precisa começar e terminar no mesmo dia.");
        }

        var faixas = profissional.Disponibilidades
            .Where(d => d.DiaSemana == (int)localInicio.DayOfWeek)
            .OrderBy(d => d.Inicio)
            .ToList();
        if (faixas.Count == 0)
        {
            throw new InvalidOperationException($"{profissional.Nome} não atende neste dia da semana.");
        }

        var cabe = faixas.Any(d =>
            d.Inicio.ToTimeSpan().TotalMinutes <= minutoInicio
            && minutoFim <= d.Fim.ToTimeSpan().TotalMinutes);
        if (!cabe)
        {
            var texto = string.Join(", ", faixas.Select(d => $"{HhMm(d.Inicio)}–{HhMm(d.Fim)}"));
            throw new InvalidOperationException($"Fora do expediente de {profissional.Nome} neste dia ({texto}).");
        }
    }

    private async Task GarantirSemBloqueioAsync(
        int empresaId,
        int profissionalId,
        DateTime inicio,
        DateTime fim,
        CancellationToken ct)
    {
        var bloqueio = await db.Bloqueios.AsNoTracking()
            .Where(b => b.EmpresaId == empresaId
                && (b.ProfissionalId == null || b.ProfissionalId == profissionalId)
                && b.Inicio < fim
                && b.Fim > inicio)
            .OrderBy(b => b.Inicio)
            .FirstOrDefaultAsync(ct);
        if (bloqueio is not null)
        {
            throw new InvalidOperationException(bloqueio.Motivo is null
                ? "Este horário está bloqueado na agenda."
                : $"Este horário está bloqueado na agenda: {bloqueio.Motivo}.");
        }
    }

    private async Task GarantirLivreAsync(
        int empresaId,
        Profissional profissional,
        DateTime inicio,
        DateTime fim,
        int intervaloApos,
        int? ignorarId,
        CancellationToken ct)
    {
        var profissionalId = profissional.Id;
        var ate = fim.AddMinutes(intervaloApos);
        var desde = inicio.AddMinutes(-MaxIntervaloApos);
        var q = db.Agendamentos.AsNoTracking().Where(a =>
            a.EmpresaId == empresaId
            && a.ProfissionalId == profissionalId
            && a.Status != StatusAgendamento.Cancelado
            && a.Inicio < ate
            && a.Fim > desde);
        if (ignorarId is int id)
        {
            q = q.Where(a => a.Id != id);
        }

        var candidatos = await q
            .Select(a => new Ocupacao(a.ProfissionalId, a.Inicio, a.Fim, a.IntervaloAposMinutos))
            .ToListAsync(ct);
        if (candidatos.Any(o => o.Inicio < fim && o.Fim > inicio))
        {
            throw new InvalidOperationException($"{profissional.Nome} já tem outro agendamento neste intervalo.");
        }
        if (candidatos.Any(o => Conflita(o, inicio, fim, intervaloApos)))
        {
            throw new InvalidOperationException(
                $"Muito perto de outro agendamento de {profissional.Nome}: falta o tempo de preparo entre atendimentos.");
        }
    }

    /// <summary>
    /// Serializa quem grava na agenda do mesmo profissional: sem isso, dois pedidos simultâneos
    /// passam pela checagem de conflito antes de qualquer um gravar. A trava solta no fim da transação.
    /// </summary>
    private async Task<IDbContextTransaction> TravarAgendaAsync(int empresaId, int profissionalId, CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({empresaId}, {profissionalId})", ct);
        return tx;
    }

    /// <summary>Cada agendamento ocupa de Inicio até Fim + preparo.</summary>
    private static bool Conflita(Ocupacao o, DateTime inicio, DateTime fim, int intervaloApos) =>
        o.Inicio < fim.AddMinutes(intervaloApos) && o.Fim.AddMinutes(o.IntervaloAposMinutos) > inicio;

    private static DateTime ParaUtc(DateTime local, TimeZoneInfo fuso) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), fuso);

    private static List<AgendamentoItem> MontarItens(IReadOnlyList<Servico> servicos) =>
        servicos.Select((s, i) => new AgendamentoItem
        {
            ServicoId = s.Id,
            Ordem = i,
            DuracaoMinutos = s.DuracaoMinutos,
            Preco = s.Preco,
        }).ToList();

    private static string HhMm(TimeOnly t) => t.ToString("HH\\:mm");

    private static AgendamentoDto Map(Agendamento a)
    {
        var itens = a.Itens.OrderBy(i => i.Ordem).ThenBy(i => i.Id).ToList();
        var servicos = itens.Select(i => new AgendamentoServicoDto(
            i.ServicoId,
            i.Servico.Nome,
            i.DuracaoMinutos,
            i.Preco)).ToList();
        return new(
            a.Id,
            a.EmpresaId,
            a.ClienteId,
            a.Cliente.Nome,
            a.Cliente.Telefone,
            servicos,
            string.Join(" + ", servicos.Select(s => s.Nome)),
            servicos.Sum(s => s.DuracaoMinutos),
            servicos.Sum(s => s.Preco),
            a.ProfissionalId,
            a.Profissional.Nome,
            a.Profissional.Cor,
            a.Inicio,
            a.Fim,
            a.Status,
            a.Observacoes,
            a.DataCriacao,
            a.DataAtualizacao,
            a.IntervaloAposMinutos,
            a.RemarcadoDeId,
            a.MotivoCancelamento);
    }
}
