using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lyra.Infrastructure.Persistence;

/// <summary>
/// Banco de negócio do Lyra. Identidade (usuarios/empresas) permanece no Core.
/// Schema padrão: lyra.
/// </summary>
public sealed class LyraDbContext(DbContextOptions<LyraDbContext> options) : DbContext(options)
{
    public DbSet<Servico> Servicos => Set<Servico>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("lyra");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LyraDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
