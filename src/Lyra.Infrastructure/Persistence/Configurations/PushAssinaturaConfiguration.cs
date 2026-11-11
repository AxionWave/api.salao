using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class PushAssinaturaConfiguration : IEntityTypeConfiguration<PushAssinatura>
{
    public void Configure(EntityTypeBuilder<PushAssinatura> builder)
    {
        builder.ToTable("push_assinaturas");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id");
        builder.Property(x => x.UsuarioId).HasColumnName("usuario_id");
        builder.Property(x => x.Endpoint).HasColumnName("endpoint").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.P256dh).HasColumnName("p256dh").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Auth).HasColumnName("auth").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Aparelho).HasColumnName("aparelho").HasMaxLength(300);
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.Property(x => x.UltimoEnvio).HasColumnName("ultimo_envio");
        builder.HasIndex(x => x.Endpoint).IsUnique().HasDatabaseName("ux_push_assinaturas_endpoint");
        builder.HasIndex(x => x.EmpresaId).HasDatabaseName("ix_push_assinaturas_empresa");
    }
}
