using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Identity.Api.Endpoints;
using Sextante.Modules.Identity.Domain.Entities;

namespace Sextante.Modules.Identity.Api;

public static class IdentityApi
{
    /// <summary>
    /// Nome da rate-limit policy aplicada ao route group <c>/api/auth/*</c>.
    /// Registada em <c>Program.cs</c> via <c>AddRateLimiter</c>.
    /// </summary>
    public const string AuthRateLimitPolicy = "auth";

    /// <summary>
    /// Mapeia <c>/api/auth/*</c> com endpoints built-in do Identity
    /// (login, refresh, logout, manage/info, forgotPassword, resetPassword,
    /// confirmEmail, resendConfirmationEmail) + signup customizado.
    /// </summary>
    /// <remarks>
    /// O endpoint built-in <c>POST /register</c> é desativado: cria
    /// <see cref="AppUser"/> sem Tenant/Membership e devolve um bearer
    /// token válido sem claim <c>tenant_id</c> — bypass de tenancy.
    /// <c>/signup</c> é o único caminho público para criar utilizadores.
    /// </remarks>
    public static IEndpointRouteBuilder MapIdentityModule(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/api/auth")
            .RequireRateLimiting(AuthRateLimitPolicy);

        var identityEndpoints = auth.MapIdentityApi<AppUser>();

        // Substitui o RequestDelegate do /register pela resposta 404. A
        // convention corre quando o endpoint é finalizado, antes da app
        // arrancar — qualquer hit em POST /register fica curto-circuitado.
        identityEndpoints.Add(builder =>
        {
            if (builder is RouteEndpointBuilder reb
                && reb.RoutePattern.RawText is { } pattern
                && pattern.EndsWith("/register", StringComparison.Ordinal))
            {
                reb.RequestDelegate = static context =>
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                };
            }
        });

        auth.MapSignup();

        return routes;
    }
}
