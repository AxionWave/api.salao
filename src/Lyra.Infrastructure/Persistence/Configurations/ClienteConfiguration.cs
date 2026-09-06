using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("clientes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(x => x.Nome).HasColumnName("nome").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Telefone).HasColumnName("telefone").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(160);
        builder.Property(x => x.Observacoes).HasColumnName("observacoes").HasMaxLength(1000);
        builder.Property(x => x.Ativo).HasColumnName("ativo").IsRequired();
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.Property(x => x.DataAtualizacao).HasColumnName("data_atualizacao");
        builder.HasIndex(x => x.EmpresaId).HasDatabaseName("ix_clientes_empresa_id");
        builder.HasIndex(x => new { x.EmpresaId, x.Telefone })
            .IsUnique()
            .HasDatabaseName("ux_clientes_empresa_telefone");
        builder.HasIndex(x => new { x.EmpresaId, x.Email })
            .IsUnique()
            .HasFilter("email IS NOT NULL")
            .HasDatabaseName("ux_clientes_empresa_email");
    }
}
