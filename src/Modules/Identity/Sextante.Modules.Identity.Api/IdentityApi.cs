using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Identity.Api.Endpoints;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Auth;

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
        auth.MapMe();
        auth.MapLogout();

        return routes;
    }

    /// <summary>
    /// Phase 1b — devolve o perfil do utilizador autenticado, incluindo
    /// claims de tenancy (<c>tenant_id</c>, <c>tenant_role</c>) que estão
    /// no <c>ClaimsPrincipal</c> mas não são acessíveis via cliente
    /// porque o bearer token de Phase 1a é opaque (encrypted ticket, não
    /// JWT — esse é Phase 6). O frontend chama este endpoint após login
    /// para popular o <c>AuthState</c>.
    /// </summary>
    private static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/me", (
                System.Security.Claims.ClaimsPrincipal user,
                Microsoft.AspNetCore.Identity.UserManager<AppUser> userManager,
                Sextante.Modules.Identity.Infrastructure.Persistence.IdentityDbContext db,
                CancellationToken ct) => GetMeAsync(user, userManager, db, ct))
            .RequireAuthorization()
            .WithName("Me")
            .WithSummary("Devolve o perfil do utilizador autenticado (id, email, tenant).")
            .Produces<MeResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    private static async Task<IResult> GetMeAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        Microsoft.AspNetCore.Identity.UserManager<AppUser> userManager,
        Sextante.Modules.Identity.Infrastructure.Persistence.IdentityDbContext db,
        CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var tenantIdClaim = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        var tenantRoleClaim = principal.FindFirst(IdentityClaimTypes.TenantRole)?.Value ?? "Member";

        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Results.Problem(
                title: "Tenant claim missing",
                detail: "O token autenticado não contém claim tenant_id válido.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var tenantName = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);

        return Results.Ok(new MeResponse(
            UserId: user.Id,
            Email: user.Email ?? string.Empty,
            TenantId: tenantId,
            TenantName: tenantName,
            TenantRole: tenantRoleClaim));
    }

    /// <summary>
    /// Phase 1b — endpoint logout que devolve 204 No Content. O middleware
    /// <c>RefreshTokenCookieMiddleware</c> em <c>Sextante.Host</c> intercepta
    /// a resposta e adiciona <c>Set-Cookie: refresh_token=; Max-Age=0</c>,
    /// apagando o cookie no browser. <c>MapIdentityApi</c> não inclui
    /// <c>/logout</c> (auth bearer é stateless); o endpoint existe apenas
    /// para o frontend ter um sítio para "encerrar sessão".
    /// </summary>
    private static IEndpointRouteBuilder MapLogout(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/logout", () => Results.NoContent())
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Termina a sessão (apaga o cookie refresh_token).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized);

        return routes;
    }

    public sealed record MeResponse(
        Guid UserId,
        string Email,
        Guid TenantId,
        string? TenantName,
        string TenantRole);
}
