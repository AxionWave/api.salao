namespace Lyra.Infrastructure.Persistence.Entities;

/// <summary>Quem atende o serviço. Serviço sem nenhum vínculo é atendido por todos.</summary>
public sealed class ServicoProfissional
{
    public int ServicoId { get; set; }
    public int ProfissionalId { get; set; }

    public Servico Servico { get; set; } = null!;
    public Profissional Profissional { get; set; } = null!;
}
