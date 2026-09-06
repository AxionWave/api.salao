using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class ProfissionalConfiguration : IEntityTypeConfiguration<Profissional>
{
    public void Configure(EntityTypeBuilder<Profissional> builder)
    {
        builder.ToTable("profissionais");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(x => x.PessoaId).HasColumnName("pessoa_id");
        builder.Property(x => x.Nome).HasColumnName("apelido").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Cor).HasColumnName("cor").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Ativo).HasColumnName("ativo").IsRequired();
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.Property(x => x.DataAtualizacao).HasColumnName("data_atualizacao");
        builder.HasIndex(x => x.EmpresaId).HasDatabaseName("ix_profissionais_empresa_id");
        builder.HasIndex(x => new { x.EmpresaId, x.PessoaId })
            .IsUnique()
            .HasFilter("pessoa_id IS NOT NULL")
            .HasDatabaseName("ux_profissionais_empresa_pessoa");
        builder.HasIndex(x => new { x.EmpresaId, x.Nome })
            .IsUnique()
            .HasDatabaseName("ux_profissionais_empresa_apelido");
        builder.HasMany(x => x.Disponibilidades)
            .WithOne(x => x.Profissional)
            .HasForeignKey(x => x.ProfissionalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
