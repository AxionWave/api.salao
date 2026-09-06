using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class AgendamentoItemConfiguration : IEntityTypeConfiguration<AgendamentoItem>
{
    public void Configure(EntityTypeBuilder<AgendamentoItem> builder)
    {
        builder.ToTable("agendamento_itens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.AgendamentoId).HasColumnName("agendamento_id").IsRequired();
        builder.Property(x => x.ServicoId).HasColumnName("servico_id").IsRequired();
        builder.Property(x => x.Ordem).HasColumnName("ordem").IsRequired();
        builder.Property(x => x.DuracaoMinutos).HasColumnName("duracao_minutos").IsRequired();
        builder.Property(x => x.Preco).HasColumnName("preco").HasPrecision(12, 2).IsRequired();

        builder.HasIndex(x => x.AgendamentoId).HasDatabaseName("ix_agendamento_itens_agendamento_id");
        builder.HasIndex(x => new { x.AgendamentoId, x.Ordem }).HasDatabaseName("ix_agendamento_itens_agendamento_ordem");

        builder.HasOne(x => x.Agendamento)
            .WithMany(x => x.Itens)
            .HasForeignKey(x => x.AgendamentoId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Servico)
            .WithMany()
            .HasForeignKey(x => x.ServicoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
