using Lyra.Application.Abstractions;
using Lyra.Application.Agendamentos;
using Lyra.Application.Bloqueios;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Services;

public sealed class BloqueioService(LyraDbContext db) : IBloqueioService
{
    private static readonly TimeSpan DuracaoMaxima = TimeSpan.FromDays(366);

    public async Task<ListaBloqueiosResponse> ListarAsync(
        int empresaId,
        DateTime de,
        DateTime ate,
        int? profissionalId,
        CancellationToken ct)
    {
        var inicio = DatasUtc.Normalizar(de);
        var fim = DatasUtc.Normalizar(ate);
        if (fim <= inicio)
        {
            throw new ArgumentException("O fim do período deve ser depois do início.");
        }
        if (fim - inicio > DuracaoMaxima)
        {
            throw new ArgumentException("O período pode ter no máximo um ano.");
        }

        var q = db.Bloqueios.AsNoTracking()
            .Include(b => b.Profissional)
            .Where(b => b.EmpresaId == empresaId && b.Inicio < fim && b.Fim > inicio);
        if (profissionalId is int pid)
        {
            q = q.Where(b => b.ProfissionalId == null || b.ProfissionalId == pid);
        }

        var itens = await q.OrderBy(b => b.Inicio).ToListAsync(ct);
        return new ListaBloqueiosResponse(itens.Select(Map).ToList());
    }

    public async Task<CriarBloqueioResponse> CriarAsync(int empresaId, SalvarBloqueioRequest request, CancellationToken ct)
    {
        var inicio = DatasUtc.Normalizar(request.Inicio);
        var fim = DatasUtc.Normalizar(request.Fim);
        if (fim <= inicio)
        {
            throw new ArgumentException("O fim do bloqueio deve ser depois do início.");
        }
        if (fim - inicio > DuracaoMaxima)
        {
            throw new ArgumentException("Um bloqueio pode durar no máximo um ano.");
        }

        var motivo = string.IsNullOrWhiteSpace(request.Motivo) ? null : request.Motivo.Trim();
        if (motivo is { Length: > 200 })
        {
            throw new ArgumentException("O motivo deve ter no máximo 200 caracteres.");
        }

        Profissional? profissional = null;
        if (request.ProfissionalId is int pid)
        {
            profissional = await db.Profissionais.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == pid && p.EmpresaId == empresaId, ct)
                ?? throw new ArgumentException("Profissional não encontrado nesta empresa.");
        }

        var entidade = new Bloqueio
        {
            EmpresaId = empresaId,
            ProfissionalId = profissional?.Id,
            Inicio = inicio,
            Fim = fim,
            Motivo = motivo,
            DataCriacao = DateTime.UtcNow,
        };
        db.Bloqueios.Add(entidade);
        await db.SaveChangesAsync(ct);

        var profissionalId = profissional?.Id;
        var afetados = await db.Agendamentos.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId
                && a.Status != StatusAgendamento.Cancelado
                && a.Status != StatusAgendamento.Concluido
                && a.Status != StatusAgendamento.NoShow
                && a.Inicio < fim
                && a.Fim > inicio
                && (profissionalId == null || a.ProfissionalId == profissionalId))
            .CountAsync(ct);

        entidade.Profissional = profissional;
        return new CriarBloqueioResponse(Map(entidade), afetados);
    }

    public async Task ExcluirAsync(int empresaId, int id, CancellationToken ct)
    {
        var entidade = await db.Bloqueios.FirstOrDefaultAsync(b => b.Id == id && b.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Bloqueio não encontrado.");
        db.Bloqueios.Remove(entidade);
        await db.SaveChangesAsync(ct);
    }

    private static BloqueioDto Map(Bloqueio b) => new(
        b.Id,
        b.ProfissionalId,
        b.Profissional?.Nome,
        b.Inicio,
        b.Fim,
        b.Motivo,
        b.DataCriacao);
}
