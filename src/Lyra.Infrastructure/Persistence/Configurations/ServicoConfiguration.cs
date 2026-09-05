using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class ServicoConfiguration : IEntityTypeConfiguration<Servico>
{
    public void Configure(EntityTypeBuilder<Servico> builder)
    {
        builder.ToTable("servicos");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(x => x.Nome).HasColumnName("nome").HasMaxLength(120).IsRequired();
        builder.Property(x => x.DuracaoMinutos).HasColumnName("duracao_minutos").IsRequired();
        builder.Property(x => x.Preco).HasColumnName("preco").HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.Categoria).HasColumnName("categoria").HasMaxLength(80);
        builder.Property(x => x.Ativo).HasColumnName("ativo").IsRequired();
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.Property(x => x.DataAtualizacao).HasColumnName("data_atualizacao");
        builder.HasIndex(x => x.EmpresaId).HasDatabaseName("ix_servicos_empresa_id");
        builder.HasIndex(x => new { x.EmpresaId, x.Nome })
            .IsUnique()
            .HasDatabaseName("ux_servicos_empresa_nome");
    }
}
