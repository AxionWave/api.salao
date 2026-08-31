using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Persistence;

/// <summary>
/// Banco de negócio do Lyra. Identidade (usuarios/empresas) permanece no Core.
/// Schema padrão: lyra.
/// </summary>
public sealed class LyraDbContext(DbContextOptions<LyraDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("lyra");
        base.OnModelCreating(modelBuilder);
    }
}
