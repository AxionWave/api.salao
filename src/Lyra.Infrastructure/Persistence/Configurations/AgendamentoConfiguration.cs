using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class AgendamentoConfiguration : IEntityTypeConfiguration<Agendamento>
{
    public void Configure(EntityTypeBuilder<Agendamento> builder)
    {
        builder.ToTable("agendamentos");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(x => x.ClienteId).HasColumnName("cliente_id").IsRequired();
        builder.Property(x => x.ProfissionalId).HasColumnName("profissional_id").IsRequired();
        builder.Property(x => x.Inicio).HasColumnName("inicio").IsRequired();
        builder.Property(x => x.Fim).HasColumnName("fim").IsRequired();
        builder.Property(x => x.IntervaloAposMinutos).HasColumnName("intervalo_apos_minutos").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Observacoes).HasColumnName("observacoes").HasMaxLength(1000);
        builder.Property(x => x.RemarcadoDeId).HasColumnName("remarcado_de_id");
        builder.Property(x => x.MotivoCancelamento).HasColumnName("motivo_cancelamento").HasMaxLength(200);
        builder.HasIndex(x => x.RemarcadoDeId).HasDatabaseName("ix_agendamentos_remarcado_de_id");
        builder.HasOne<Agendamento>()
            .WithMany()
            .HasForeignKey(x => x.RemarcadoDeId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Property(x => x.ContaClienteId).HasColumnName("conta_cliente_id");
        builder.HasIndex(x => x.ContaClienteId).HasDatabaseName("ix_agendamentos_conta_cliente_id");
        builder.HasOne<ContaCliente>()
            .WithMany()
            .HasForeignKey(x => x.ContaClienteId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.Property(x => x.DataAtualizacao).HasColumnName("data_atualizacao");

        builder.HasIndex(x => x.EmpresaId).HasDatabaseName("ix_agendamentos_empresa_id");
        builder.HasIndex(x => new { x.EmpresaId, x.ProfissionalId, x.Inicio, x.Fim })
            .HasDatabaseName("ix_agendamentos_empresa_profissional_intervalo");
        builder.HasIndex(x => new { x.EmpresaId, x.Inicio })
            .HasDatabaseName("ix_agendamentos_empresa_inicio");

        builder.HasOne(x => x.Cliente)
            .WithMany()
            .HasForeignKey(x => x.ClienteId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Profissional)
            .WithMany()
            .HasForeignKey(x => x.ProfissionalId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
