using Microsoft.EntityFrameworkCore;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Lookup das moedas ativas via <see cref="IdentityDbContext"/>. Cache
/// scoped (por request) — uma chamada por request basta.
/// </summary>
public sealed class CurrencyDirectory : ICurrencyDirectory
{
    private readonly IdentityDbContext _db;
    private IReadOnlyList<string>? _cachedCodes;

    public CurrencyDirectory(IdentityDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> GetActiveCodesAsync(CancellationToken cancellationToken)
    {
        if (_cachedCodes is not null)
        {
            return _cachedCodes;
        }

        _cachedCodes = await _db.Currencies
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.Code)
            .OrderBy(code => code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return _cachedCodes;
    }

    public async Task<bool> IsActiveAsync(string code, CancellationToken cancellationToken)
    {
        if (!Currency.IsValidCode(code))
        {
            return false;
        }

        var codes = await GetActiveCodesAsync(cancellationToken).ConfigureAwait(false);
        return codes.Contains(code, StringComparer.Ordinal);
    }
}
