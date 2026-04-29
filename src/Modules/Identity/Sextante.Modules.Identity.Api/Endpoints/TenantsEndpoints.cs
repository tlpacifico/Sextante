using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Api.Endpoints;

public sealed record TenantSettingsResponse(Guid Id, string Name, string PrimaryCurrency);

public sealed record UpdateTenantSettingsRequest(string PrimaryCurrency);

public static class TenantsEndpoints
{
    public static IEndpointRouteBuilder MapTenantsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/tenants/me").RequireAuthorization();

        group.MapGet("", async (
            ITenantContext tenantContext,
            IdentityDbContext db,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.TenantId.Value;
            var tenant = await db.Tenants
                .Where(t => t.Id == tenantId)
                .Select(t => new TenantSettingsResponse(t.Id, t.Name, t.PrimaryCurrency))
                .FirstOrDefaultAsync(ct);
            return tenant is null ? Results.NotFound() : Results.Ok(tenant);
        });

        group.MapPut("", async (
            [FromBody] UpdateTenantSettingsRequest request,
            ITenantContext tenantContext,
            IdentityDbContext db,
            CancellationToken ct) =>
        {
            var code = request.PrimaryCurrency?.Trim().ToUpperInvariant() ?? string.Empty;

            if (!Currency.IsValidCode(code))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["primaryCurrency"] = ["Código ISO 4217 inválido."],
                });
            }

            var isActive = await db.Currencies.AnyAsync(c => c.Code == code && c.IsActive, ct);
            if (!isActive)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["primaryCurrency"] = [$"Moeda '{code}' não está ativa."],
                });
            }

            var tenantId = tenantContext.TenantId.Value;
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (tenant is null)
            {
                return Results.NotFound();
            }

            tenant.PrimaryCurrency = code;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new TenantSettingsResponse(tenant.Id, tenant.Name, tenant.PrimaryCurrency));
        });

        return routes;
    }
}
