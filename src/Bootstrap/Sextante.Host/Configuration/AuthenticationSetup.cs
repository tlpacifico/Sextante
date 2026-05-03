using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sextante.Modules.Identity.Api;

namespace Sextante.Host.Configuration;

internal static class AuthenticationSetup
{
    public static void AddSextanteAuthentication(
        this WebApplicationBuilder builder)
    {
        EnsureJwtSigningKey(builder.Configuration, builder.Environment);

        builder.Services.AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme, options =>
            {
                options.BearerTokenExpiration = TimeSpan.FromMinutes(15);

                var extendedLifetime = builder.Configuration.GetValue(
                    "Auth:RefreshTokenLifetimeExtended",
                    TimeSpan.FromDays(30));
                var defaultLifetime = builder.Configuration.GetValue(
                    "Auth:RefreshTokenLifetime",
                    TimeSpan.FromDays(7));

                // Usa o lifetime máximo como default do Identity; o cookie
                // Max-Age controla o tempo que o browser mantém o cookie.
                // "Manter-me ligado" usa o lifetime estendido no cookie.
                options.RefreshTokenExpiration = extendedLifetime > defaultLifetime
                    ? extendedLifetime
                    : defaultLifetime;
            });
        builder.Services.AddAuthorization();

        // Rate limiting nos endpoints /api/auth/* — defesa contra brute-force,
        // password spray e enumeração. Particionado por IP do cliente; janela
        // fixa de 1 minuto. Limit deliberadamente folgado: 30 req/IP/min cobre
        // workflow legítimo (signup → login → refresh) e bloqueia spray.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(IdentityApi.AuthRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                        ?? httpContext.Request.Headers["X-Forwarded-For"].ToString()
                        ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });
    }

    private static void EnsureJwtSigningKey(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var fromEnv = configuration["JWT:SIGNING_KEY"]
            ?? configuration["JWT__SIGNING_KEY"];

        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return;
        }

        // Em Production fail-fast: ASPNETCORE_ENVIRONMENT pode ser flippado para
        // Development por engano (env var herdada, .env errado), e gerar uma
        // chave random no disco mascararia o problema.
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "JWT__SIGNING_KEY ausente. Configura a env var antes de arrancar o Host. "
                + "Gera com `openssl rand -base64 64`.");
        }

        // Em Development gera/persiste uma chave em <ContentRoot>/dev-jwt-key.bin
        // (gitignored — ver .gitignore). Estável entre reinícios para que tokens
        // emitidos sobrevivam a um restart do `dotnet run`. Ver .env.example.
        var keyPath = Path.Combine(environment.ContentRootPath, "dev-jwt-key.bin");
        string key;
        if (File.Exists(keyPath))
        {
            key = File.ReadAllText(keyPath).Trim();
        }
        else
        {
            key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            File.WriteAllText(keyPath, key);
        }

        configuration["JWT__SIGNING_KEY"] = key;
    }
}
