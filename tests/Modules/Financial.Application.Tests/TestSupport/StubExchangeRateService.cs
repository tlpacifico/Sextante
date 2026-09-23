using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Application.Tests.TestSupport;

/// <summary>Sem câmbio: todas as moedas tratadas como a primária ("1.0 implied").</summary>
public sealed class StubExchangeRateService : IExchangeRateService
{
    public Task<ExchangeRateSnapshot?> ResolveAsync(
        string fromCurrency, string toCurrency, DateTimeOffset at, CancellationToken cancellationToken)
        => Task.FromResult<ExchangeRateSnapshot?>(null);
}
