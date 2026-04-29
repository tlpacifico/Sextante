using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Application.ExchangeRates;

/// <summary>
/// Resolve o snapshot de câmbio usado por <c>CreateTransactionHandler</c>
/// no momento da criação. Implementação canónica é EUR-base
/// (ECB publica EUR-base nativamente; cross-rate <c>X→Y</c> via
/// <c>(1 / EUR→X) × EUR→Y</c>).
/// </summary>
public interface IExchangeRateService
{
    /// <summary>
    /// Devolve o snapshot quando <paramref name="fromCurrency"/> !=
    /// <paramref name="toCurrency"/>; <c>null</c> quando são iguais
    /// (signal "rate = 1.0 implied"). Lança
    /// <see cref="Sextante.Modules.Financial.Domain.Common.ExchangeRateUnavailableException"/>
    /// quando não há rate disponível.
    /// </summary>
    Task<ExchangeRateSnapshot?> ResolveAsync(
        string fromCurrency,
        string toCurrency,
        DateTimeOffset at,
        CancellationToken cancellationToken);
}
