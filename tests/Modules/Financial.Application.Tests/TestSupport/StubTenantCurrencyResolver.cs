using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Financial.Application.Tests.TestSupport;

public sealed class StubTenantCurrencyResolver : ITenantCurrencyResolver
{
    private readonly string _currency;

    public StubTenantCurrencyResolver(string currency = "EUR") => _currency = currency;

    public Task<string> GetPrimaryCurrencyAsync(CancellationToken cancellationToken) => Task.FromResult(_currency);
}
