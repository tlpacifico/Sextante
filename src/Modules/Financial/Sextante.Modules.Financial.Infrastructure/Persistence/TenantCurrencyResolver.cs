using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Financial.Infrastructure.Persistence;

/// <summary>
/// Lê <c>shared.Tenants.PrimaryCurrency</c> para o tenant ativo. Cache
/// scoped — uma chamada por request basta. Phase 3 substitui por uma
/// variante que aceita override por transação.
/// </summary>
public sealed class TenantCurrencyResolver : ITenantCurrencyResolver
{
    private readonly IdentityDbContext _identity;
    private readonly ITenantContext _tenant;
    private string? _cached;

    public TenantCurrencyResolver(IdentityDbContext identity, ITenantContext tenant)
    {
        _identity = identity;
        _tenant = tenant;
    }

    public async Task<string> GetPrimaryCurrencyAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var tenantId = _tenant.TenantId.Value;
        var currency = await _identity.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.PrimaryCurrency)
            .FirstOrDefaultAsync(cancellationToken);

        _cached = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency;
        return _cached;
    }
}
