using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Application.Features.Budgets;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Financial.Infrastructure.Budgets;

/// <summary>
/// Calcula <see cref="BudgetProgress"/> on-demand. Faz uma query
/// agregadora sobre <c>financial.transactions</c> filtrada por
/// (category, occurred_at em [period.Start, period.End]) e converte
/// cada row para a moeda do <c>Budget.Limit</c>.
/// Cache local de rates via dicionário (currency, day) evita N+1
/// no caso double-leg (Budget≠primary≠tx). Em ECB miss, marca
/// <c>RateMissing=true</c> e degrada graciosamente — Phase 5b
/// requirements §Decisions.
/// </summary>
public sealed class BudgetProgressService : IBudgetProgressService
{
    private readonly FinancialDbContext _db;
    private readonly IExchangeRateService _exchangeRates;
    private readonly ITenantCurrencyResolver _tenantCurrency;

    public BudgetProgressService(
        FinancialDbContext db,
        IExchangeRateService exchangeRates,
        ITenantCurrencyResolver tenantCurrency)
    {
        _db = db;
        _exchangeRates = exchangeRates;
        _tenantCurrency = tenantCurrency;
    }

    public async Task<BudgetProgress> CalculateAsync(
        Budget budget,
        DateOnly? asOfDate,
        CancellationToken cancellationToken)
    {
        var primaryCurrency = await _tenantCurrency.GetPrimaryCurrencyAsync(cancellationToken);
        var budgetCurrency = budget.Limit.Currency;

        var periodStart = budget.Period.Start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        // End-exclusive: < periodEndExclusive captura todo o último dia.
        var periodEndExclusive = budget.Period.End.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await _db.Transactions
            .Where(t => t.CategoryId == budget.CategoryId
                        && t.OccurredAt >= periodStart
                        && t.OccurredAt < periodEndExclusive)
            .Select(t => new TxRow(
                t.Amount.Amount,
                t.Amount.Currency,
                t.ExchangeRateToPrimary,
                t.OccurredAt))
            .ToListAsync(cancellationToken);

        var rateCache = new Dictionary<(string Currency, DateOnly Date), decimal?>();
        var contributions = new List<TransactionContribution>(rows.Count);

        foreach (var row in rows)
        {
            var occurredDate = DateOnly.FromDateTime(row.OccurredAt.UtcDateTime);
            var (amount, missing) = await ResolveContributionAsync(
                row, budgetCurrency, primaryCurrency, rateCache, cancellationToken);

            contributions.Add(new TransactionContribution(amount, missing, occurredDate));
        }

        var today = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return BudgetProgressCalculator.Calculate(budget, contributions, today);
    }

    private async Task<(decimal Amount, bool RateMissing)> ResolveContributionAsync(
        TxRow row,
        string budgetCurrency,
        string primaryCurrency,
        Dictionary<(string, DateOnly), decimal?> rateCache,
        CancellationToken ct)
    {
        // Caso 1: tx em moeda do budget → soma direta.
        if (string.Equals(row.Currency, budgetCurrency, StringComparison.Ordinal))
        {
            return (row.Amount, false);
        }

        // Caso 2: budget em primary → usa exchange_rate_to_primary
        // gravado na tx.
        if (string.Equals(budgetCurrency, primaryCurrency, StringComparison.Ordinal))
        {
            if (row.ExchangeRateToPrimary is null)
            {
                return (0m, true);
            }

            return (row.Amount * row.ExchangeRateToPrimary.Value, false);
        }

        // Caso 3: budget em moeda diferente da primary E da tx →
        // double-leg: tx → primary → budget.
        decimal txInPrimary;
        if (string.Equals(row.Currency, primaryCurrency, StringComparison.Ordinal))
        {
            txInPrimary = row.Amount;
        }
        else
        {
            if (row.ExchangeRateToPrimary is null)
            {
                return (0m, true);
            }
            txInPrimary = row.Amount * row.ExchangeRateToPrimary.Value;
        }

        var occurredDate = DateOnly.FromDateTime(row.OccurredAt.UtcDateTime);
        var key = (budgetCurrency, occurredDate);

        if (!rateCache.TryGetValue(key, out var primaryToBudgetRate))
        {
            try
            {
                var snapshot = await _exchangeRates.ResolveAsync(
                    primaryCurrency,
                    budgetCurrency,
                    row.OccurredAt,
                    ct);
                primaryToBudgetRate = snapshot?.Rate ?? 1m;
            }
            catch (ExchangeRateUnavailableException)
            {
                primaryToBudgetRate = null;
            }

            rateCache[key] = primaryToBudgetRate;
        }

        if (primaryToBudgetRate is null)
        {
            return (0m, true);
        }

        return (txInPrimary * primaryToBudgetRate.Value, false);
    }

    private sealed record TxRow(
        decimal Amount,
        string Currency,
        decimal? ExchangeRateToPrimary,
        DateTimeOffset OccurredAt);
}
