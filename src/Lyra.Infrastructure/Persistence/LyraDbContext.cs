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
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Familia> Familias => Set<Familia>();
    public DbSet<Profissional> Profissionais => Set<Profissional>();
    public DbSet<Disponibilidade> Disponibilidades => Set<Disponibilidade>();
    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();
    public DbSet<AgendamentoItem> AgendamentoItens => Set<AgendamentoItem>();
    public DbSet<EmpresaConfig> EmpresaConfigs => Set<EmpresaConfig>();
    public DbSet<Bloqueio> Bloqueios => Set<Bloqueio>();
    public DbSet<ServicoProfissional> ServicoProfissionais => Set<ServicoProfissional>();
    public DbSet<Notificacao> Notificacoes => Set<Notificacao>();
    public DbSet<PainelLeitura> PainelLeituras => Set<PainelLeitura>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("lyra");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LyraDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
