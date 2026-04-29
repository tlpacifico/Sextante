using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Api.Endpoints;

public sealed record CurrencyResponse(
    string Code,
    string Name,
    string Symbol,
    int MinorUnits,
    bool IsActive);

public sealed record CreateCurrencyRequest(
    string Code,
    string Name,
    string Symbol,
    int MinorUnits,
    bool IsActive);

public sealed record UpdateCurrencyRequest(
    string Name,
    string Symbol,
    int MinorUnits,
    bool IsActive);

public static class CurrenciesEndpoints
{
    public static IEndpointRouteBuilder MapCurrenciesEndpoints(this IEndpointRouteBuilder routes)
    {
        // Lookup público autenticado (forms de Account / Transaction).
        var publicGroup = routes.MapGroup("/api/currencies").RequireAuthorization();

        publicGroup.MapGet("", async (IdentityDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Currencies
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.Code)
                .Select(c => new CurrencyResponse(c.Code, c.Name, c.Symbol, c.MinorUnits, c.IsActive))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        // Admin CRUD.
        var adminGroup = routes.MapGroup("/api/admin/currencies")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "SystemAdmin" });

        adminGroup.MapGet("", async (IdentityDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Currencies
                .AsNoTracking()
                .OrderBy(c => c.Code)
                .Select(c => new CurrencyResponse(c.Code, c.Name, c.Symbol, c.MinorUnits, c.IsActive))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        adminGroup.MapPost("", async (
            [FromBody] CreateCurrencyRequest request,
            IdentityDbContext db,
            CancellationToken ct) =>
        {
            var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

            if (await db.Currencies.AnyAsync(c => c.Code == code, ct))
            {
                return Results.Conflict(new ProblemDetails
                {
                    Title = "Conflito",
                    Detail = $"Moeda '{code}' já existe.",
                    Status = StatusCodes.Status409Conflict,
                });
            }

            try
            {
                var currency = Currency.Create(code, request.Name, request.Symbol, request.MinorUnits, request.IsActive);
                db.Currencies.Add(currency);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/admin/currencies/{currency.Code}",
                    new CurrencyResponse(currency.Code, currency.Name, currency.Symbol, currency.MinorUnits, currency.IsActive));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["currency"] = [ex.Message],
                });
            }
        });

        adminGroup.MapPut("{code}", async (
            string code,
            [FromBody] UpdateCurrencyRequest request,
            IdentityDbContext db,
            CancellationToken ct) =>
        {
            var normalized = code?.Trim().ToUpperInvariant() ?? string.Empty;
            var currency = await db.Currencies.FirstOrDefaultAsync(c => c.Code == normalized, ct);
            if (currency is null)
            {
                return Results.NotFound();
            }

            try
            {
                currency.Update(request.Name, request.Symbol, request.MinorUnits, request.IsActive);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new CurrencyResponse(
                    currency.Code, currency.Name, currency.Symbol, currency.MinorUnits, currency.IsActive));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["currency"] = [ex.Message],
                });
            }
        });

        return routes;
    }
}
