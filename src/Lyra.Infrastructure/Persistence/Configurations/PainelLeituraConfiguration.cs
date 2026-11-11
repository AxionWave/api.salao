using Lyra.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lyra.Infrastructure.Persistence.Configurations;

public sealed class PainelLeituraConfiguration : IEntityTypeConfiguration<PainelLeitura>
{
    public void Configure(EntityTypeBuilder<PainelLeitura> builder)
    {
        builder.ToTable("painel_leituras");
        builder.HasKey(x => new { x.EmpresaId, x.UsuarioId });
        builder.Property(x => x.EmpresaId).HasColumnName("empresa_id");
        builder.Property(x => x.UsuarioId).HasColumnName("usuario_id");
        builder.Property(x => x.VistoAte).HasColumnName("visto_ate").IsRequired();
    }
}
