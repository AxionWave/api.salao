using Lyra.Application.Abstractions;
using Lyra.Infrastructure.Auth;
using Lyra.Infrastructure.Notificacoes;
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
        services.AddScoped<IConfiguracaoEmpresaService, Lyra.Infrastructure.Services.ConfiguracaoEmpresaService>();
        services.AddScoped<IBloqueioService, Lyra.Infrastructure.Services.BloqueioService>();
        services.AddScoped<INotificacaoService, NotificacaoService>();

        services.Configure<NotificacoesOptions>(configuration.GetSection(NotificacoesOptions.Secao));
        services.AddScoped<FilaNotificacoes>();
        services.AddScoped<ProcessadorNotificacoes>();
        services.AddScoped<LeitorEmpresa>();
        services.AddSingleton<LinksPublicos>();
        services.AddScoped<IAgendamentoPublicoService, AgendamentoPublicoService>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Secao));
        if (!string.IsNullOrWhiteSpace(configuration[EmailOptions.Secao + ":ApiKey"]))
        {
            services.AddHttpClient<CanalEmail>(c => c.Timeout = TimeSpan.FromSeconds(20));
            services.AddScoped<ICanalNotificacao>(sp => sp.GetRequiredService<CanalEmail>());
        }
        if (configuration.GetValue(NotificacoesOptions.Secao + ":Worker", true))
        {
            services.AddHostedService<WorkerNotificacoes>();
        }

        var conn = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(conn))
        {
            services.AddDbContext<LyraDbContext>(o =>
                o.UseNpgsql(conn, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "lyra")));
        }

        return services;
    }
}
