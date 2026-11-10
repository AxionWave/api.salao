using System.Text.Json;
using Lyra.Application.Abstractions;
using Lyra.Application.Configuracao;
using Lyra.Infrastructure.Persistence;
using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Services;

public sealed class ConfiguracaoEmpresaService(LyraDbContext db) : IConfiguracaoEmpresaService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ConfiguracaoEmpresaDto> ObterAsync(int empresaId, CancellationToken ct)
    {
        var entidade = await db.EmpresaConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmpresaId == empresaId, ct);
        return Map(entidade);
    }

    public async Task<ConfiguracaoEmpresaDto> SalvarAsync(int empresaId, SalvarConfiguracaoRequest request, CancellationToken ct)
    {
        var segmento = Segmentos.Obter(request.Segmento)
            ?? throw new ArgumentException("Segmento inválido.");
        var fuso = (request.FusoHorario ?? string.Empty).Trim();
        if (!Segmentos.FusoValido(fuso))
        {
            throw new ArgumentException("Fuso horário inválido.");
        }

        string? terminologia = null;
        if (request.Terminologia is { } termos)
        {
            var normalizada = Normalizar(termos);
            if (normalizada != segmento.Terminologia)
            {
                terminologia = JsonSerializer.Serialize(normalizada, Json);
            }
        }

        var entidade = await db.EmpresaConfigs.FirstOrDefaultAsync(x => x.EmpresaId == empresaId, ct);
        if (entidade is null)
        {
            entidade = new EmpresaConfig { EmpresaId = empresaId, DataCriacao = DateTime.UtcNow };
            db.EmpresaConfigs.Add(entidade);
        }
        else
        {
            entidade.DataAtualizacao = DateTime.UtcNow;
        }
        entidade.Segmento = segmento.Id;
        entidade.FusoHorario = fuso;
        entidade.Terminologia = terminologia;

        await db.SaveChangesAsync(ct);
        return Map(entidade);
    }

    private static ConfiguracaoEmpresaDto Map(EmpresaConfig? e)
    {
        var segmento = Segmentos.Obter(e?.Segmento) ?? Segmentos.Obter(Segmentos.Padrao)!;
        var personalizada = Ler(e?.Terminologia);
        return new ConfiguracaoEmpresaDto(
            segmento.Id,
            e?.FusoHorario ?? Segmentos.FusoPadrao,
            personalizada ?? segmento.Terminologia,
            personalizada is not null,
            e?.DataAtualizacao ?? e?.DataCriacao);
    }

    private static TerminologiaDto? Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<TerminologiaDto>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TerminologiaDto Normalizar(TerminologiaDto t) => new(
        Termo(t.Recurso, "Recurso"),
        Termo(t.Servico, "Serviço"),
        Termo(t.Cliente, "Cliente"),
        Termo(t.Agendamento, "Agendamento"));

    private static TermoDto Termo(TermoDto? termo, string campo)
    {
        var singular = (termo?.Singular ?? string.Empty).Trim();
        var plural = (termo?.Plural ?? string.Empty).Trim();
        var genero = (termo?.Genero ?? string.Empty).Trim().ToLowerInvariant();
        if (singular.Length == 0 || plural.Length == 0)
        {
            throw new ArgumentException($"{campo}: informe singular e plural.");
        }
        if (singular.Length > 40 || plural.Length > 40)
        {
            throw new ArgumentException($"{campo}: no máximo 40 caracteres.");
        }
        if (genero is not ("m" or "f"))
        {
            throw new ArgumentException($"{campo}: gênero deve ser masculino ou feminino.");
        }
        return new TermoDto(singular, plural, genero);
    }
}
