using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Domain.Entities;

/// <summary>
/// Snapshot de taxa de câmbio. EUR-base canónico (ECB publica EUR-base
/// nativamente; cross-rate <c>X→Y</c> em runtime). Reference data
/// partilhada cross-tenant — sem RLS. Imutabilidade: rate é escrita
/// uma vez e atualizada apenas via upsert idempotente do snapshot
/// diário ou inserção manual.
/// </summary>
public sealed class ExchangeRate : IVersioned
{
    public const string EurBase = "EUR";
    public const string SourceEcb = "ECB";
    public const string SourceManual = "manual";

    private ExchangeRate()
    {
        FromCurrency = EurBase;
        ToCurrency = string.Empty;
        Source = SourceEcb;
    }

    public Guid Id { get; private set; }
    public DateOnly RateDate { get; private set; }
    public string FromCurrency { get; private set; }
    public string ToCurrency { get; private set; }
    public decimal Rate { get; private set; }
    public string Source { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }

    public static ExchangeRate Create(
        DateOnly rateDate,
        string fromCurrency,
        string toCurrency,
        decimal rate,
        string source)
    {
        if (!Currency.IsValidCode(fromCurrency))
        {
            throw new ArgumentException(
                $"FromCurrency '{fromCurrency}' inválido — esperado ISO 4217.",
                nameof(fromCurrency));
        }

        if (!Currency.IsValidCode(toCurrency))
        {
            throw new ArgumentException(
                $"ToCurrency '{toCurrency}' inválido — esperado ISO 4217.",
                nameof(toCurrency));
        }

        if (rate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate tem de ser positivo.");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source é obrigatório.", nameof(source));
        }

        return new ExchangeRate
        {
            Id = GuidV7.NewId(),
            RateDate = rateDate,
            FromCurrency = fromCurrency,
            ToCurrency = toCurrency,
            Rate = rate,
            Source = source,
        };
    }

    public void UpdateRate(decimal newRate, string source)
    {
        if (newRate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(newRate), "Rate tem de ser positivo.");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source é obrigatório.", nameof(source));
        }

        Rate = newRate;
        Source = source;
    }
}
