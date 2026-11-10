using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class BloqueioConfiguration : IEntityTypeConfiguration<Bloqueio>
{
    public void Configure(EntityTypeBuilder<Bloqueio> builder)
    {
        builder.ToTable("bloqueios");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(x => x.ProfissionalId).HasColumnName("profissional_id");
        builder.Property(x => x.Inicio).HasColumnName("inicio").IsRequired();
        builder.Property(x => x.Fim).HasColumnName("fim").IsRequired();
        builder.Property(x => x.Motivo).HasColumnName("motivo").HasMaxLength(200);
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.HasOne(x => x.Profissional)
            .WithMany()
            .HasForeignKey(x => x.ProfissionalId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.EmpresaId, x.Inicio, x.Fim }).HasDatabaseName("ix_bloqueios_empresa_periodo");
    }
}
