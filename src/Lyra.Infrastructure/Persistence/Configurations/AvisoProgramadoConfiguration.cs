using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class AvisoProgramadoConfiguration : IEntityTypeConfiguration<AvisoProgramado>
{
    public void Configure(EntityTypeBuilder<AvisoProgramado> builder)
    {
        builder.ToTable("avisos_programados");
        builder.HasKey(x => x.Chave);
        builder.Property(x => x.Chave).HasColumnName("chave").HasMaxLength(120);
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id");
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.HasIndex(x => x.DataCriacao).HasDatabaseName("ix_avisos_programados_data");
    }
}
