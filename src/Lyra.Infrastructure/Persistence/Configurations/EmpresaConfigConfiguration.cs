using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class EmpresaConfigConfiguration : IEntityTypeConfiguration<EmpresaConfig>
{
    public void Configure(EntityTypeBuilder<EmpresaConfig> builder)
    {
        builder.ToTable("empresa_config");
        builder.HasKey(x => x.EmpresaId);
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id").ValueGeneratedNever();
        builder.Property(x => x.Segmento).HasColumnName("segmento").HasMaxLength(30).IsRequired();
        builder.Property(x => x.FusoHorario).HasColumnName("fuso_horario").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Terminologia).HasColumnName("terminologia").HasColumnType("jsonb");
        builder.Property(x => x.EnviarEmails).HasColumnName("enviar_emails").HasDefaultValue(false);
        builder.Property(x => x.LembretesHoras).HasColumnName("lembretes_horas").HasDefaultValueSql("'{24}'::integer[]").IsRequired();
        builder.Property(x => x.ResumoDiario).HasColumnName("resumo_diario").HasDefaultValue(false);
        builder.Property(x => x.Avisos).HasColumnName("avisos").HasColumnType("jsonb");
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.Property(x => x.DataAtualizacao).HasColumnName("data_atualizacao");
    }
}
