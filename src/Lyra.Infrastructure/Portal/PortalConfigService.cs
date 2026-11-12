using Lyra.Application.Portal;
using Lyra.Infrastructure.Notificacoes;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Portal;

public sealed class PortalConfigService(LyraDbContext db, LeitorEmpresa empresas) : IPortalConfigService
{
    public async Task<PortalConfigResponse> ObterAsync(int empresaId, CancellationToken ct)
    {
        var config = await db.EmpresaConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.EmpresaId == empresaId, ct);
        return await RespostaAsync(empresaId, config, ct);
    }

    public async Task<PortalConfigResponse> SalvarAsync(int empresaId, PortalConfigDto request, CancellationToken ct)
    {
        var endereco = PortalEmpresa.NormalizarEndereco(request.Endereco);
        if (request.Ativo && endereco is null)
        {
            throw new ArgumentException("Escolha o endereço da página para ligar o agendamento online.");
        }
        if (request.AntecedenciaMinutos is < 0 or > 10080)
        {
            throw new ArgumentException("Antecedência: de 0 a 7 dias.");
        }
        if (request.JanelaDias is < 1 or > 365) throw new ArgumentException("Dias à frente: de 1 a 365.");
        if (request.LimiteAbertos is < 1 or > 20) throw new ArgumentException("Horários marcados ao mesmo tempo: de 1 a 20.");
        if (!PortalEmpresa.Passos.Contains(request.PassoMinutos))
        {
            throw new ArgumentException($"Intervalo entre horários: {string.Join(", ", PortalEmpresa.Passos)} minutos.");
        }
        var apresentacao = string.IsNullOrWhiteSpace(request.Apresentacao) ? null : request.Apresentacao.Trim();
        if (apresentacao is { Length: > 500 }) throw new ArgumentException("Apresentação: até 500 caracteres.");

        if (endereco is not null && await EmUsoAsync(empresaId, endereco, ct))
        {
            throw new InvalidOperationException("Este endereço já é de outra empresa. Escolha outro.");
        }

        var servicos = await db.Servicos.Where(s => s.EmpresaId == empresaId).Select(s => s.Id).ToListAsync(ct);
        var profissionais = await db.Profissionais.Where(p => p.EmpresaId == empresaId).Select(p => p.Id).ToListAsync(ct);
        var portal = new PortalEmpresa
        {
            Ativo = request.Ativo,
            Apresentacao = apresentacao,
            AntecedenciaMinutos = request.AntecedenciaMinutos,
            JanelaDias = request.JanelaDias,
            LimiteAbertos = request.LimiteAbertos,
            PassoMinutos = request.PassoMinutos,
            ServicosOcultos = (request.ServicosOcultos ?? []).Distinct().Where(servicos.Contains).Order().ToList(),
            ProfissionaisOcultos = (request.ProfissionaisOcultos ?? []).Distinct().Where(profissionais.Contains).Order().ToList(),
        };

        var config = await db.EmpresaConfigs.FirstOrDefaultAsync(c => c.EmpresaId == empresaId, ct);
        if (config is null)
        {
            config = new EmpresaConfig { EmpresaId = empresaId };
            db.EmpresaConfigs.Add(config);
        }
        config.PortalEndereco = endereco;
        config.Portal = portal.Serializar();
        config.DataAtualizacao = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("Este endereço acabou de ser escolhido por outra empresa. Escolha outro.");
        }
        return await RespostaAsync(empresaId, config, ct);
    }

    public async Task<EnderecoDisponivelResponse> DisponivelAsync(int empresaId, string? endereco, CancellationToken ct)
    {
        string? e;
        try
        {
            e = PortalEmpresa.NormalizarEndereco(endereco);
        }
        catch (ArgumentException ex)
        {
            return new EnderecoDisponivelResponse(false, ex.Message);
        }
        if (e is null) return new EnderecoDisponivelResponse(false, "Escolha um endereço.");
        return await EmUsoAsync(empresaId, e, ct)
            ? new EnderecoDisponivelResponse(false, "Já é de outra empresa.")
            : new EnderecoDisponivelResponse(true, null);
    }

    private Task<bool> EmUsoAsync(int empresaId, string endereco, CancellationToken ct) =>
        db.EmpresaConfigs.AnyAsync(c => c.PortalEndereco == endereco && c.EmpresaId != empresaId, ct);

    private async Task<PortalConfigResponse> RespostaAsync(int empresaId, EmpresaConfig? config, CancellationToken ct)
    {
        var portal = PortalEmpresa.Ler(config?.Portal);
        string? sugestao = null;
        if (config?.PortalEndereco is null && PortalEmpresa.Sugerir((await empresas.ObterAsync(empresaId, ct))?.Nome) is { } slug)
        {
            for (var i = 1; i <= 9 && sugestao is null; i++)
            {
                var candidato = i == 1 ? slug : $"{slug[..Math.Min(slug.Length, 37)].TrimEnd('-')}-{i}";
                if (!await EmUsoAsync(empresaId, candidato, ct)) sugestao = candidato;
            }
        }
        var servicos = await db.Servicos.AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Ativo)
            .OrderBy(s => s.Nome)
            .Select(s => new ItemPortalDto(s.Id, s.Nome))
            .ToListAsync(ct);
        var profissionais = await db.Profissionais.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.Ativo)
            .OrderBy(p => p.Nome)
            .Select(p => new ItemPortalDto(p.Id, p.Nome))
            .ToListAsync(ct);
        return new PortalConfigResponse(portal.Dto(config?.PortalEndereco), sugestao, servicos, profissionais);
    }
}
