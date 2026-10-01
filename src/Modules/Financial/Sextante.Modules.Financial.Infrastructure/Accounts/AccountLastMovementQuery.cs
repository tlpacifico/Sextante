using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Application.Features.Accounts;
using Sextante.Modules.Financial.Infrastructure.Persistence;

namespace Sextante.Modules.Financial.Infrastructure.Accounts;

public sealed class AccountLastMovementQuery(FinancialDbContext db) : IAccountLastMovementQuery
{
    public async Task<DateOnly?> GetLastMovementDateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var last = await db.Transactions
            .Where(t => t.AccountId == accountId)
            .MaxAsync(t => (DateTimeOffset?)t.OccurredAt, cancellationToken);

        return last is { } value ? DateOnly.FromDateTime(value.UtcDateTime) : null;
    }
}
