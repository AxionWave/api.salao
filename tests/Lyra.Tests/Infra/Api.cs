using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Lyra.Core.Modules;
using Microsoft.IdentityModel.Tokens;

namespace Lyra.Tests.Infra;

public sealed record Resposta(HttpStatusCode Status, JsonElement Json, string Texto)
{
    public int Codigo => (int)Status;
    public string Mensagem =>
        Json.ValueKind == JsonValueKind.Object && Json.TryGetProperty("message", out var m) ? m.GetString() ?? "" : Texto;
    public int Id => Json.GetProperty("id").GetInt32();

    public override string ToString() => $"{Codigo}: {Texto}";
}

/// <summary>Cliente HTTP com o token de um usuário. Corpos e respostas em JSON camelCase.</summary>
public sealed class Api(HttpClient http, string token)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<Resposta> Get(string rota) => Enviar(HttpMethod.Get, rota);
    public Task<Resposta> Post(string rota, object corpo) => Enviar(HttpMethod.Post, rota, corpo);
    public Task<Resposta> Put(string rota, object corpo) => Enviar(HttpMethod.Put, rota, corpo);
    public Task<Resposta> Patch(string rota, object corpo) => Enviar(HttpMethod.Patch, rota, corpo);
    public Task<Resposta> Delete(string rota) => Enviar(HttpMethod.Delete, rota);

    public async Task<Resposta> Enviar(HttpMethod metodo, string rota, object? corpo = null)
    {
        using var pedido = new HttpRequestMessage(metodo, rota);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (corpo is not null) pedido.Content = JsonContent.Create(corpo, options: Json);
        using var resposta = await http.SendAsync(pedido);
        var texto = await resposta.Content.ReadAsStringAsync();
        var json = default(JsonElement);
        if (!string.IsNullOrWhiteSpace(texto))
        {
            try { json = JsonDocument.Parse(texto).RootElement.Clone(); } catch (JsonException) { }
        }
        return new Resposta(resposta.StatusCode, json, texto);
    }
}

public static class Tokens
{
    public static readonly string[] Todos = ModuleCodes.Todos;
    public static readonly string[] Recepcao = [ModuleCodes.Raiz, ModuleCodes.Agenda];

    public static string Gerar(int? empresaId, IEnumerable<string> modulos, string? segredo = null, int usuarioId = 1)
    {
        var claims = new List<Claim>
        {
            new("userId", usuarioId.ToString()),
            new("username", "testes"),
            new("email", "testes@lyra.local"),
            new("roles", "USER"),
        };
        if (empresaId is int id) claims.Add(new Claim("empresaId", id.ToString()));
        claims.AddRange(modulos.Select(m => new Claim("modulos", m)));

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(segredo ?? ApiFixture.Segredo));
        var jwt = new JwtSecurityToken(
            issuer: "testes",
            audience: "testes",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
