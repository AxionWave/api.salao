using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Lyra.API.Auth;
using Lyra.Application.Portal;
using Lyra.Infrastructure;
using Lyra.Infrastructure.AoVivo;
using Lyra.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

// Sem DSN (local e testes) o Sentry fica desligado.
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = sentryDsn;
        o.Environment = builder.Configuration["Sentry:Environment"] ?? builder.Environment.EnvironmentName.ToLowerInvariant();
        o.Release = builder.Configuration["Sentry:Release"];
        // Dados de clientes (nome, telefone) não devem sair para o Sentry.
        o.SendDefaultPii = false;
        o.EnableLogs = true;
        o.TracesSampler = ctx =>
            ctx.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase) ? 0.0
            : builder.Environment.IsDevelopment() ? 1.0
            : 0.2;
    });
}

builder.Services.AddScoped<Lyra.API.Exceptions.ApiExceptionFilter>();
builder.Services.AddControllers(o => o.Filters.Add<Lyra.API.Exceptions.ApiExceptionFilter>())
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Lyra API",
        Version = "v1",
        Description = "API de negócio do Lyra (Salão). Autenticação = JWT Enterprise (oAuth via Gateway)."
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddInfrastructure(builder.Configuration);

var jwtSecret = new[] { Environment.GetEnvironmentVariable("JWT_SECRET"), builder.Configuration["Jwt:Secret"] }
    .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Defina JWT_SECRET: a API não sobe sem a chave dos tokens.");
    }
    // Em Development a assinatura não é validada (abaixo); a chave só precisa existir.
    jwtSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
}
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
var tokensCliente = new TokensCliente(jwtSecret);
builder.Services.AddSingleton<ITokensCliente>(tokensCliente);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "username",
            RoleClaimType = "roles"
        };
        // WebSocket e SSE do navegador não mandam cabeçalho: o cliente SignalR põe o token na URL.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/api/salao/hub"))
                {
                    ctx.Token = token;
                }
                return Task.CompletedTask;
            }
        };
        if (builder.Environment.IsDevelopment())
        {
            // Token vem do oAuth da VPS; a chave local não é a de produção.
            options.UseSecurityTokenValidators = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = false;
            options.TokenValidationParameters.RequireSignedTokens = false;
            options.TokenValidationParameters.SignatureValidator = (token, _) =>
                new JwtSecurityTokenHandler().ReadJwtToken(token);
        }
    })
    .AddJwtBearer(TokensCliente.Esquema, options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = tokensCliente.Validacao;
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR(o => o.EnableDetailedErrors = builder.Environment.IsDevelopment())
    .AddJsonProtocol(o => o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddCors(o =>
{
    o.AddPolicy("dev", p => p
        .SetIsOriginAllowed(origin =>
            origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetService<LyraDbContext>();
    db?.Database.Migrate();
}
catch (Exception ex) when (app.Environment.IsDevelopment())
{
    app.Logger.LogWarning(ex, "Schema lyra não migrado (Postgres indisponível).");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("dev");
}

// Sem buffer no Nginx: com SSE (quando o WebSocket não passa) as mensagens chegam na hora.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/salao/hub"))
    {
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<PainelHub>(PainelHub.Rota);

app.Run();

/// <summary>Visível para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
