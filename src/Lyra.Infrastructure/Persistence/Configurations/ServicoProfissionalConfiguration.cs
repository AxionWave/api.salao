using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class ServicoProfissionalConfiguration : IEntityTypeConfiguration<ServicoProfissional>
{
    public void Configure(EntityTypeBuilder<ServicoProfissional> builder)
    {
        builder.ToTable("servico_profissionais");
        builder.HasKey(x => new { x.ServicoId, x.ProfissionalId });
        builder.Property(x => x.ServicoId).HasColumnName("servico_id");
        builder.Property(x => x.ProfissionalId).HasColumnName("profissional_id");
        builder.HasOne(x => x.Servico)
            .WithMany(s => s.Profissionais)
            .HasForeignKey(x => x.ServicoId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Profissional)
            .WithMany()
            .HasForeignKey(x => x.ProfissionalId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ProfissionalId).HasDatabaseName("ix_servico_profissionais_profissional");
    }
}
