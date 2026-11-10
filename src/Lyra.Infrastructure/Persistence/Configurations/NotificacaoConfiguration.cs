using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class NotificacaoConfiguration : IEntityTypeConfiguration<Notificacao>
{
    public void Configure(EntityTypeBuilder<Notificacao> builder)
    {
        builder.ToTable("notificacoes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(x => x.Evento).HasColumnName("evento").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Canal).HasColumnName("canal").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Destino).HasColumnName("destino").HasMaxLength(200);
        builder.Property(x => x.ClienteId).HasColumnName("cliente_id");
        builder.Property(x => x.AgendamentoId).HasColumnName("agendamento_id");
        builder.Property(x => x.Dados).HasColumnName("dados").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Tentativas).HasColumnName("tentativas").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ProximaTentativa).HasColumnName("proxima_tentativa").IsRequired();
        builder.Property(x => x.UltimoErro).HasColumnName("ultimo_erro").HasMaxLength(500);
        builder.Property(x => x.ProvedorId).HasColumnName("provedor_id").HasMaxLength(200);
        builder.Property(x => x.DataCriacao).HasColumnName("data_criacao").IsRequired();
        builder.Property(x => x.DataEnvio).HasColumnName("data_envio");

        builder.HasIndex(x => x.ProximaTentativa)
            .HasDatabaseName("ix_notificacoes_pendentes")
            .HasFilter("status = 'pendente'");
        builder.HasIndex(x => new { x.EmpresaId, x.ClienteId, x.DataCriacao })
            .HasDatabaseName("ix_notificacoes_empresa_cliente");

        builder.HasOne(x => x.Agendamento)
            .WithMany()
            .HasForeignKey(x => x.AgendamentoId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Cliente>()
            .WithMany()
            .HasForeignKey(x => x.ClienteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
