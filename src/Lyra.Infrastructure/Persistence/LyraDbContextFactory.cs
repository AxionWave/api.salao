using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lyra.Infrastructure.Persistence;

public sealed class LyraDbContextFactory : IDesignTimeDbContextFactory<LyraDbContext>
{
    public LyraDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LyraDbContext>()
            .UseNpgsql(
                "Host=195.35.18.119;Port=5433;Database=base;Username=postgres;Password=724200;Search Path=lyra",
                npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "lyra"))
            .Options;
        return new LyraDbContext(options);
    }
}
