namespace Lyra.Infrastructure.Notificacoes;

public sealed class EmailOptions
{
    public const string Secao = "Email";

    /// <summary>AW.Mensageria, que entrega os e-mails (rede Docker interna).</summary>
    public string UrlMensageria { get; set; } = "http://mensageria:8080";

    /// <summary>
    /// Token das chamadas internas (<c>GATEWAY_INTERNAL_TOKEN</c>). Vazio = o canal de e-mail não existe nesta instalação.
    /// </summary>
    public string TokenInterno { get; set; } = "";

    /// <summary>Onde o front atende os links públicos (<c>/c/{token}</c>).</summary>
    public string UrlPublica { get; set; } = "https://lyra.lumensoft.com.br";

    /// <summary>Gateway que serve as imagens públicas do AW.Arquivos (logo da empresa). Vazio = e-mail sem logo.</summary>
    public string UrlArquivos { get; set; } = "https://api.lumensoft.com.br";
}
