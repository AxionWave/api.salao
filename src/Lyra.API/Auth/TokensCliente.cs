using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Lyra.Application.Portal;
using Microsoft.IdentityModel.Tokens;

namespace Lyra.API.Auth;

/// <summary>
/// Emite e valida o token de quem entrou no portal pelo código do e-mail. A chave é derivada do JWT_SECRET,
/// então um token de cliente não passa nas rotas da equipe e um token da equipe não passa no portal.
/// </summary>
public sealed class TokensCliente : ITokensCliente
{
    public const string Esquema = "Cliente";
    public const string Emissor = "lyra";
    public const string Audiencia = "lyra-portal";
    public static readonly TimeSpan Validade = TimeSpan.FromDays(30);

    public TokensCliente(string segredo)
    {
        Chave = new SymmetricSecurityKey(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), "lyra-portal-cliente-v1"u8));
    }

    public SymmetricSecurityKey Chave { get; }

    public TokenValidationParameters Validacao => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = Chave,
        ValidIssuer = Emissor,
        ValidAudience = Audiencia,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
    };

    public (string Token, DateTime Expira) Emitir(int contaId, string email)
    {
        var expira = DateTime.UtcNow.Add(Validade);
        var jwt = new JwtSecurityToken(
            issuer: Emissor,
            audience: Audiencia,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, contaId.ToString()), new Claim(JwtRegisteredClaimNames.Email, email)],
            expires: expira,
            signingCredentials: new SigningCredentials(Chave, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expira);
    }

    /// <summary>Id da conta no token já validado pelo esquema <see cref="Esquema"/>.</summary>
    public static int? ContaId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) && id > 0 ? id : null;
}
