namespace Lyra.Application.Perfis;

public sealed class PerfilAcessoLyraDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string? Descricao { get; set; }
    public bool Ativo { get; set; }
}
