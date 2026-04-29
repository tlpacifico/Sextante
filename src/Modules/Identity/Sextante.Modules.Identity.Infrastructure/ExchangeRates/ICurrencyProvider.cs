namespace Sextante.Modules.Identity.Infrastructure.ExchangeRates;

/// <summary>
/// Abstrai a fonte de cotações cambiais. MVP: ECB
/// (<see cref="EcbCurrencyProvider"/>); Phase 8 expande para crypto/equities.
/// </summary>
public interface ICurrencyProvider
{
    Task<EcbSnapshot> FetchLatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Snapshot diário ECB. <c>RateDate</c> em UTC; <c>Rates</c> com base
/// EUR (ECB publica EUR-base nativamente).
/// </summary>
public sealed record EcbSnapshot(DateOnly RateDate, IReadOnlyList<EcbDailyRate> Rates);

public sealed record EcbDailyRate(string ToCurrency, decimal Rate);

/// <summary>
/// Fail-loud quando o provider não consegue parsear ou contactar ECB.
/// </summary>
public sealed class EcbProviderException : Exception
{
    public EcbProviderException(string message) : base(message)
    {
    }

    public EcbProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
