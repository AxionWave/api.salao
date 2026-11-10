namespace Lyra.Infrastructure.Services;

internal static class DatasUtc
{
    /// <summary>Data sem fuso (Kind=Unspecified) é tratada como UTC, igual ao que o front envia em ISO.</summary>
    public static DateTime Normalizar(DateTime valor) => valor.Kind switch
    {
        DateTimeKind.Utc => valor,
        DateTimeKind.Local => valor.ToUniversalTime(),
        _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc),
    };
}
