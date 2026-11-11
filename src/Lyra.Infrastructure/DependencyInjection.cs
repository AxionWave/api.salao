using Lyra.Application.Abstractions;
using Lyra.Infrastructure.AoVivo;
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
        services.AddSingleton<SinalNotificacoes>();
        services.AddSingleton<AvisosAoVivo>();
        services.AddScoped<ICanalNotificacao, CanalPainel>();
        services.AddScoped<IPainelService, PainelService>();
        services.AddScoped<ProcessadorNotificacoes>();
        services.AddScoped<ProgramadorAvisos>();
        services.AddScoped<LeitorEmpresa>();
        services.AddSingleton<LinksPublicos>();
        services.AddScoped<IAgendamentoPublicoService, AgendamentoPublicoService>();

        var tokenInterno = Environment.GetEnvironmentVariable("GATEWAY_INTERNAL_TOKEN")
            ?? configuration[EmailOptions.Secao + ":TokenInterno"];
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Secao));
        services.PostConfigure<EmailOptions>(o => o.TokenInterno = tokenInterno ?? "");
        if (!string.IsNullOrWhiteSpace(tokenInterno))
        {
            services.AddHttpClient<CanalEmail>(c => c.Timeout = TimeSpan.FromSeconds(20));
            services.AddScoped<ICanalNotificacao>(sp => sp.GetRequiredService<CanalEmail>());
        }
        if (configuration.GetValue(NotificacoesOptions.Secao + ":Worker", true))
        {
            services.AddHostedService<WorkerNotificacoes>();
            services.AddHostedService<AgendadorAvisos>();
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
