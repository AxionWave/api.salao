using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class CodigoAcessoConfiguration : IEntityTypeConfiguration<CodigoAcesso>
{
    public void Configure(EntityTypeBuilder<CodigoAcesso> builder)
    {
        builder.ToTable("codigos_acesso");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(160).IsRequired();
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id");
        builder.Property(x => x.CodigoHash).HasColumnName("codigo_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Expira).HasColumnName("expira").IsRequired();
        builder.Property(x => x.Tentativas).HasColumnName("tentativas");
        builder.Property(x => x.UsadoEm).HasColumnName("usado_em");
        builder.Property(x => x.Ip).HasColumnName("ip").HasMaxLength(64);
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.HasIndex(x => new { x.Email, x.DataCriacao }).HasDatabaseName("ix_codigos_acesso_email");
        builder.HasIndex(x => new { x.Ip, x.DataCriacao }).HasDatabaseName("ix_codigos_acesso_ip");
    }
}
