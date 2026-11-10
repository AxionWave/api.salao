using Lyra.Application.Configuracao;
using Lyra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Services;

internal static class FusoHorarioEmpresa
{
    public static async Task<TimeZoneInfo> ObterAsync(LyraDbContext db, int empresaId, CancellationToken ct)
    {
        var id = await db.EmpresaConfigs.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .Select(c => c.FusoHorario)
            .FirstOrDefaultAsync(ct);
        return Resolver(id ?? Segmentos.FusoPadrao);
    }

    public static TimeZoneInfo Resolver(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Sem base de fusos no host (ex.: container sem tzdata): Brasília não tem horário de verão desde 2019.
            return TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(-3), id, id);
        }
    }
}
