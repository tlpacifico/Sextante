using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Identity.Infrastructure.Persistence;

namespace Sextante.Modules.Financial.Infrastructure.ExchangeRates;

/// <summary>
/// Lookup contra <c>shared.exchange_rates</c> com cross-rate EUR-base.
/// Casos:
/// <list type="bullet">
/// <item><c>from == to</c> ⇒ <c>null</c> (rate=1.0 implied).</item>
/// <item><c>from == EUR</c> ⇒ <c>EUR→to</c> direto.</item>
/// <item><c>to == EUR</c> ⇒ <c>1 / EUR→from</c>.</item>
/// <item>nenhum é EUR ⇒ <c>(1 / EUR→from) × EUR→to</c>.</item>
/// </list>
/// </summary>
public sealed class ExchangeRateService : IExchangeRateService
{
    private const string EurBase = CrossRate.EurBase;

    private readonly IdentityDbContext _identity;

    public ExchangeRateService(IdentityDbContext identity)
    {
        _identity = identity;
    }

    public async Task<ExchangeRateSnapshot?> ResolveAsync(
        string fromCurrency,
        string toCurrency,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (string.Equals(fromCurrency, toCurrency, StringComparison.Ordinal))
        {
            return null;
        }

        var rateDate = DateOnly.FromDateTime(at.UtcDateTime);
        var rate = await ResolveCrossRateAsync(fromCurrency, toCurrency, rateDate, cancellationToken);
        return new ExchangeRateSnapshot(rate, at);
    }

    private async Task<decimal> ResolveCrossRateAsync(
        string from,
        string to,
        DateOnly rateDate,
        CancellationToken cancellationToken)
    {
        decimal? eurFrom = string.Equals(from, EurBase, StringComparison.Ordinal)
            ? null
            : await GetEurRateAsync(from, rateDate, cancellationToken);

        decimal? eurTo = string.Equals(to, EurBase, StringComparison.Ordinal)
            ? null
            : await GetEurRateAsync(to, rateDate, cancellationToken);

        return CrossRate.Compute(from, to, eurFrom, eurTo);
    }

    private async Task<decimal> GetEurRateAsync(
        string toCurrency,
        DateOnly rateDate,
        CancellationToken cancellationToken)
    {
        // Procura linha exata; se ausente, faz fallback para a rate
        // mais recente até à data (fim-de-semana, feriados — ECB não
        // publica todos os dias). Se não há nada, lança.
        var exact = await _identity.ExchangeRates
            .AsNoTracking()
            .Where(r => r.RateDate == rateDate
                && r.FromCurrency == EurBase
                && r.ToCurrency == toCurrency)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(cancellationToken);

        if (exact is not null)
        {
            return exact.Value;
        }

        var fallback = await _identity.ExchangeRates
            .AsNoTracking()
            .Where(r => r.RateDate <= rateDate
                && r.FromCurrency == EurBase
                && r.ToCurrency == toCurrency)
            .OrderByDescending(r => r.RateDate)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(cancellationToken);

        if (fallback is not null)
        {
            return fallback.Value;
        }

        throw new ExchangeRateUnavailableException(EurBase, toCurrency, rateDate);
    }
}
