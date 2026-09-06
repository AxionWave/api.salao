using Lyra.Application.Abstractions;
using Lyra.Infrastructure.Auth;
using Lyra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lyra.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();
        services.AddScoped<IServicoService, Lyra.Infrastructure.Services.ServicoService>();
        services.AddScoped<IClienteService, Lyra.Infrastructure.Services.ClienteService>();
        services.AddScoped<IProfissionalService, Lyra.Infrastructure.Services.ProfissionalService>();
        services.AddScoped<IAgendamentoService, Lyra.Infrastructure.Services.AgendamentoService>();
        services.AddScoped<IPerfilLyraService, Lyra.Infrastructure.Services.PerfilLyraService>();

        var conn = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(conn))
        {
            services.AddDbContext<LyraDbContext>(o =>
                o.UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "lyra")));
        }

        return services;
    }
}
