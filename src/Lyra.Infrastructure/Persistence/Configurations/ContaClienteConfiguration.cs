using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class ContaClienteConfiguration : IEntityTypeConfiguration<ContaCliente>
{
    public void Configure(EntityTypeBuilder<ContaCliente> builder)
    {
        builder.ToTable("contas_cliente");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Nome).HasColumnName("nome").HasMaxLength(120);
        builder.Property(x => x.Telefone).HasColumnName("telefone").HasMaxLength(20);
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.Property(x => x.UltimoAcesso).HasColumnName("ultimo_acesso");
        builder.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_contas_cliente_email");
    }
}
