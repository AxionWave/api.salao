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

        var conn = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(conn))
        {
            services.AddDbContext<LyraDbContext>(o => o.UseNpgsql(conn));
        }

        return services;
    }
}
