using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Infrastructure.Jobs;
using Sextante.Modules.Identity.Infrastructure.Persistence;

namespace Sextante.Modules.Identity.Api.Endpoints;

public sealed record ExchangeRateResponse(
    Guid Id,
    DateOnly RateDate,
    string FromCurrency,
    string ToCurrency,
    decimal Rate,
    string Source,
    DateTimeOffset UpdatedAt);

public sealed record ManualExchangeRateRequest(
    DateOnly RateDate,
    string ToCurrency,
    decimal Rate);

public sealed record ExchangeRateSnapshotStateResponse(
    DateTimeOffset? LastRunAt,
    DateTimeOffset? LastSuccessAt,
    string? LastError);

public sealed class ManualExchangeRateValidator : AbstractValidator<ManualExchangeRateRequest>
{
    public ManualExchangeRateValidator()
    {
        RuleFor(r => r.RateDate)
            .Must(date => date <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Data não pode ser futura.");
        RuleFor(r => r.ToCurrency)
            .Matches("^[A-Z]{3}$")
            .WithMessage("ToCurrency deve ser ISO 4217 (3 letras maiúsculas).");
        RuleFor(r => r.Rate)
            .GreaterThan(0m)
            .WithMessage("Rate tem de ser positivo.");
    }
}

public static class ExchangeRatesEndpoints
{
    private const int DefaultDisplayDays = 7;

    public static IEndpointRouteBuilder MapExchangeRatesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/admin/exchange-rates")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "SystemAdmin" });

        group.MapGet("", async (
            [FromQuery] DateOnly? from,
            [FromQuery] DateOnly? to,
            [FromQuery] string[]? currencies,
            IConfiguration config,
            IdentityDbContext db,
            CancellationToken ct) =>
        {
            var defaults = config.GetSection("ExchangeRates:DefaultDisplayCurrencies").Get<string[]>()
                ?? new[] { "USD", "GBP", "BRL", "JPY", "CHF" };

            var endDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var startDate = from ?? endDate.AddDays(-DefaultDisplayDays);
            var displayCurrencies = currencies is { Length: > 0 } ? currencies : defaults;

            var rows = await db.ExchangeRates
                .AsNoTracking()
                .Where(r => r.RateDate >= startDate
                    && r.RateDate <= endDate
                    && displayCurrencies.Contains(r.ToCurrency))
                .OrderByDescending(r => r.RateDate)
                .ThenBy(r => r.ToCurrency)
                .Select(r => new ExchangeRateResponse(
                    r.Id, r.RateDate, r.FromCurrency, r.ToCurrency, r.Rate, r.Source, r.UpdatedAt))
                .ToListAsync(ct);

            return Results.Ok(rows);
        });

        group.MapGet("state", async (IdentityDbContext db, CancellationToken ct) =>
        {
            var state = await db.EcbSnapshotStates
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == EcbSnapshotState.SingletonId, ct);
            return Results.Ok(new ExchangeRateSnapshotStateResponse(
                state?.LastRunAt,
                state?.LastSuccessAt,
                state?.LastError));
        });

        group.MapPost("", async (
            [FromBody] ManualExchangeRateRequest request,
            IValidator<ManualExchangeRateRequest> validator,
            IdentityDbContext db,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var to = request.ToCurrency.ToUpperInvariant();
            var activeCurrency = await db.Currencies.AnyAsync(c => c.Code == to && c.IsActive, ct);
            if (!activeCurrency)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["toCurrency"] = [$"Moeda '{to}' não está ativa."],
                });
            }

            var existing = await db.ExchangeRates.FirstOrDefaultAsync(
                r => r.RateDate == request.RateDate
                    && r.FromCurrency == ExchangeRate.EurBase
                    && r.ToCurrency == to,
                ct);

            if (existing is null)
            {
                existing = ExchangeRate.Create(
                    request.RateDate,
                    ExchangeRate.EurBase,
                    to,
                    request.Rate,
                    ExchangeRate.SourceManual);
                db.ExchangeRates.Add(existing);
            }
            else
            {
                existing.UpdateRate(request.Rate, ExchangeRate.SourceManual);
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new ExchangeRateResponse(
                existing.Id, existing.RateDate, existing.FromCurrency, existing.ToCurrency,
                existing.Rate, existing.Source, existing.UpdatedAt));
        });

        group.MapPost("snapshot/run", (IBackgroundJobClient jobClient) =>
        {
            jobClient.Enqueue<EcbSnapshotJob>(job => job.RunAsync(CancellationToken.None));
            return Results.Accepted();
        });

        return routes;
    }
}
