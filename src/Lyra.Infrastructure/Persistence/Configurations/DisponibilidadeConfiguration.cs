using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class DisponibilidadeConfiguration : IEntityTypeConfiguration<Disponibilidade>
{
    public void Configure(EntityTypeBuilder<Disponibilidade> builder)
    {
        builder.ToTable("disponibilidades");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ProfissionalId).HasColumnName("profissional_id").IsRequired();
        builder.Property(x => x.DiaSemana).HasColumnName("dia_semana").IsRequired();
        builder.Property(x => x.Inicio).HasColumnName("inicio").HasColumnType("time").IsRequired();
        builder.Property(x => x.Fim).HasColumnName("fim").HasColumnType("time").IsRequired();
        builder.HasIndex(x => x.ProfissionalId).HasDatabaseName("ix_disponibilidades_profissional_id");
        builder.HasIndex(x => new { x.ProfissionalId, x.DiaSemana })
            .IsUnique()
            .HasDatabaseName("ux_disponibilidades_profissional_dia");
    }
}
