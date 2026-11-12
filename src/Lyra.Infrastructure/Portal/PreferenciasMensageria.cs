using System.Net.Http.Json;
using System.Text.Json;
using Lyra.Infrastructure.Notificacoes;
using Microsoft.Extensions.Options;

namespace Lyra.Infrastructure.Portal;

/// <summary>
/// A lista de "não quero mais receber" fica na AW.Mensageria (a mesma do link no rodapé dos e-mails).
/// Vale para o Lyra e a empresa de quem pede.
/// </summary>
public sealed class PreferenciasMensageria(HttpClient http, IOptions<EmailOptions> opcoes)
{
    public sealed record Situacao(bool Recebe, bool PediuParaSair, string? Bloqueio);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sem o token interno (local sem Mensageria) não há a quem perguntar.</summary>
    public bool Disponivel => !string.IsNullOrWhiteSpace(opcoes.Value.TokenInterno);

    public Task<Situacao> ObterAsync(int empresaId, string email, CancellationToken ct) =>
        EnviarAsync(HttpMethod.Get, empresaId, $"?canal=email&destino={Uri.EscapeDataString(email)}", null, ct);

    public Task<Situacao> DefinirAsync(int empresaId, string email, bool receber, CancellationToken ct) =>
        EnviarAsync(HttpMethod.Put, empresaId, "", new { canal = "email", destino = email, receber }, ct);

    private async Task<Situacao> EnviarAsync(HttpMethod metodo, int empresaId, string resto, object? corpo, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(metodo, $"{opcoes.Value.UrlMensageria.TrimEnd('/')}/api/mensageria/preferencias{resto}");
        if (corpo is not null) pedido.Content = JsonContent.Create(corpo, options: Json);
        pedido.Headers.Add("X-Internal-Service-Token", opcoes.Value.TokenInterno);
        pedido.Headers.Add("X-Empresa-Id", empresaId.ToString());
        pedido.Headers.Add("X-Origin-System", CanalEmail.SiglaSistema);

        using var resposta = await http.SendAsync(pedido, ct);
        var texto = await resposta.Content.ReadAsStringAsync(ct);
        if (!resposta.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Mensageria {(int)resposta.StatusCode} nas preferências: {(texto.Length > 300 ? texto[..300] : texto)}");
        }
        return JsonSerializer.Deserialize<Situacao>(texto, Json)
            ?? throw new HttpRequestException("Mensageria respondeu sem corpo nas preferências.");
    }
}
