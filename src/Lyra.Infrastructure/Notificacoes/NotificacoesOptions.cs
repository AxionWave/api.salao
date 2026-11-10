namespace Lyra.Infrastructure.Notificacoes;

public sealed class NotificacoesOptions
{
    public const string Secao = "Notificacoes";

    /// <summary>Canais ligados. Só eles geram notificações; vazio = nada é enfileirado.</summary>
    public string[] Canais { get; set; } = [];

    /// <summary>Liga o worker em segundo plano (os testes processam a fila na mão).</summary>
    public bool Worker { get; set; } = true;

    public int IntervaloSegundos { get; set; } = 10;
}
