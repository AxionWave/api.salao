using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Lyra.Infrastructure;
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

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["Jwt:Secret"]
    ?? "defaultSecretKeyForJWTTokenGenerationAndValidation";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

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
        if (builder.Environment.IsDevelopment())
        {
            // Token vem do oAuth da VPS; a chave local não é a de produção.
            options.UseSecurityTokenValidators = true;
            options.TokenValidationParameters.ValidateIssuerSigningKey = false;
            options.TokenValidationParameters.RequireSignedTokens = false;
            options.TokenValidationParameters.SignatureValidator = (token, _) =>
                new JwtSecurityTokenHandler().ReadJwtToken(token);
        }
    });

builder.Services.AddAuthorization();

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

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
