using Lyra.Application.Abstractions;
using Lyra.Application.Agendamentos;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Services;

public sealed class AgendamentoService(LyraDbContext db) : IAgendamentoService
{
    public async Task<ListaAgendamentosResponse> ListarAsync(
        int empresaId,
        DateTime de,
        DateTime ate,
        int? profissionalId,
        string? status,
        CancellationToken ct)
    {
        var inicio = AsUtc(de);
        var fim = AsUtc(ate);
        if (fim <= inicio)
        {
            throw new ArgumentException("O fim do período deve ser depois do início.");
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
            .Where(p => p.EmpresaId == empresaId && p.Ativo)
            .OrderBy(p => p.Nome)
            .ToListAsync(ct);

        var servicos = await db.Servicos.AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Ativo)
            .OrderBy(s => s.Nome)
            .ToListAsync(ct);

        return new OpcoesAgendaResponse(
            profissionais.Select(p => new ProfissionalAgendaDto(
                p.Id,
                p.Nome,
                p.Cor,
                p.Disponibilidades
                    .OrderBy(d => d.DiaSemana)
                    .Select(d => new IntervaloAgendaDto(d.DiaSemana, HhMm(d.Inicio), HhMm(d.Fim)))
                    .ToList())).ToList(),
            servicos.Select(s => new ServicoAgendaDto(s.Id, s.Nome, s.DuracaoMinutos, s.Preco)).ToList());
    }

    public async Task<AgendamentoDto?> ObterAsync(int empresaId, int id, CancellationToken ct)
    {
        var a = await QueryCompleto(empresaId).FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? null : Map(a);
    }

    public async Task<AgendamentoDto> CriarAsync(int empresaId, SalvarAgendamentoRequest request, CancellationToken ct)
    {
        var (cliente, servicos, profissional, inicio, fim, obs) = await ValidarAsync(empresaId, request, ct);
        await GarantirLivreAsync(empresaId, profissional.Id, inicio, fim, ignorarId: null, ct);

        var entidade = new Agendamento
        {
            EmpresaId = empresaId,
            ClienteId = cliente.Id,
            ProfissionalId = profissional.Id,
            Inicio = inicio,
            Fim = fim,
            Status = StatusAgendamento.Agendado,
            Observacoes = obs,
            DataCriacao = DateTime.UtcNow,
            Itens = MontarItens(servicos),
        };
        db.Agendamentos.Add(entidade);
        await db.SaveChangesAsync(ct);
        return (await ObterAsync(empresaId, entidade.Id, ct))!;
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

        var (cliente, servicos, profissional, inicio, fim, obs) = await ValidarAsync(empresaId, request, ct);
        await GarantirLivreAsync(empresaId, profissional.Id, inicio, fim, id, ct);

        entidade.ClienteId = cliente.Id;
        entidade.ProfissionalId = profissional.Id;
        entidade.Inicio = inicio;
        entidade.Fim = fim;
        entidade.Observacoes = obs;
        entidade.DataAtualizacao = DateTime.UtcNow;
        entidade.Itens.Clear();
        foreach (var item in MontarItens(servicos))
        {
            entidade.Itens.Add(item);
        }
        await db.SaveChangesAsync(ct);
        return (await ObterAsync(empresaId, id, ct))!;
    }

    public async Task<AgendamentoDto> AlterarStatusAsync(int empresaId, int id, string status, CancellationToken ct)
    {
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
        entidade.Status = novo;
        entidade.DataAtualizacao = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await ObterAsync(empresaId, id, ct))!;
    }

    private IQueryable<Agendamento> QueryCompleto(int empresaId) =>
        db.Agendamentos.AsNoTracking()
            .Include(a => a.Cliente)
            .Include(a => a.Profissional)
            .Include(a => a.Itens).ThenInclude(i => i.Servico)
            .Where(a => a.EmpresaId == empresaId);

    private async Task<(Cliente Cliente, IReadOnlyList<Servico> Servicos, Profissional Profissional, DateTime Inicio, DateTime Fim, string? Obs)>
        ValidarAsync(int empresaId, SalvarAgendamentoRequest request, CancellationToken ct)
    {
        var cliente = await db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.ClienteId && c.EmpresaId == empresaId, ct)
            ?? throw new ArgumentException("Cliente não encontrado nesta empresa.");
        if (!cliente.Ativo)
        {
            throw new ArgumentException("Cliente inativo.");
        }

        var ids = request.ServicoIds ?? [];
        if (ids.Count == 0)
        {
            throw new ArgumentException("Informe ao menos um serviço.");
        }
        if (ids.Count > 20)
        {
            throw new ArgumentException("No máximo 20 serviços por horário.");
        }

        var encontrados = await db.Servicos.AsNoTracking()
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
            if (!servico.Ativo)
            {
                throw new ArgumentException($"O serviço «{servico.Nome}» está inativo.");
            }
            if (servico.DuracaoMinutos < 1)
            {
                throw new ArgumentException($"Serviço «{servico.Nome}» com duração inválida.");
            }
            servicos.Add(servico);
        }

        var profissional = await db.Profissionais.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProfissionalId && p.EmpresaId == empresaId, ct)
            ?? throw new ArgumentException("Profissional não encontrado nesta empresa.");
        if (!profissional.Ativo)
        {
            throw new ArgumentException("Profissional inativo.");
        }

        var inicio = AsUtc(request.Inicio);
        var fim = inicio.AddMinutes(servicos.Sum(s => s.DuracaoMinutos));
        if (fim <= inicio)
        {
            throw new ArgumentException("Horário inválido.");
        }

        var obs = string.IsNullOrWhiteSpace(request.Observacoes) ? null : request.Observacoes.Trim();
        if (obs is { Length: > 1000 })
        {
            throw new ArgumentException("Observações devem ter no máximo 1000 caracteres.");
        }

        return (cliente, servicos, profissional, inicio, fim, obs);
    }

    private async Task GarantirLivreAsync(
        int empresaId,
        int profissionalId,
        DateTime inicio,
        DateTime fim,
        int? ignorarId,
        CancellationToken ct)
    {
        var q = db.Agendamentos.AsNoTracking().Where(a =>
            a.EmpresaId == empresaId
            && a.ProfissionalId == profissionalId
            && a.Status != StatusAgendamento.Cancelado
            && a.Inicio < fim
            && a.Fim > inicio);
        if (ignorarId is int id)
        {
            q = q.Where(a => a.Id != id);
        }
        if (await q.AnyAsync(ct))
        {
            throw new InvalidOperationException("Este profissional já tem um horário neste intervalo.");
        }
    }

    private static List<AgendamentoItem> MontarItens(IReadOnlyList<Servico> servicos) =>
        servicos.Select((s, i) => new AgendamentoItem
        {
            ServicoId = s.Id,
            Ordem = i,
            DuracaoMinutos = s.DuracaoMinutos,
            Preco = s.Preco,
        }).ToList();

    private static DateTime AsUtc(DateTime valor)
    {
        return valor.Kind switch
        {
            DateTimeKind.Utc => valor,
            DateTimeKind.Local => valor.ToUniversalTime(),
            _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc),
        };
    }

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
            a.DataAtualizacao);
    }
}
