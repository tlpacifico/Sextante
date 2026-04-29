namespace Sextante.Modules.Financial.Domain.Transactions;

/// <summary>
/// Cápsula imutável do câmbio capturado quando uma transação é criada.
/// <c>Rate</c> é o multiplicador de <c>Amount.Currency → tenant primary</c>;
/// <c>At</c> é o instante UTC em que o snapshot foi resolvido.
/// </summary>
public sealed record ExchangeRateSnapshot
{
    public ExchangeRateSnapshot(decimal rate, DateTimeOffset at)
    {
        if (rate <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate tem de ser positivo.");
        }

        Rate = rate;
        At = at;
    }

    public decimal Rate { get; }
    public DateTimeOffset At { get; }
}
