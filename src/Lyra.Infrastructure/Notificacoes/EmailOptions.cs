namespace Lyra.Infrastructure.Notificacoes;

public sealed class EmailOptions
{
    public const string Secao = "Email";

    /// <summary>Chave do Resend. Vazia = o canal de e-mail não existe nesta instalação.</summary>
    public string ApiKey { get; set; } = "";

    public string UrlApi { get; set; } = "https://api.resend.com";

    /// <summary>Endereço do remetente; o nome exibido é o da empresa.</summary>
    public string Remetente { get; set; } = "naoresponda@lyra.lumensoft.com.br";

    /// <summary>Onde o front atende os links públicos (<c>/c/{token}</c>).</summary>
    public string UrlPublica { get; set; } = "https://lyra.lumensoft.com.br";
}
