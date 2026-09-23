using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Application.Features.Accounts;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Infrastructure.Persistence;

namespace Sextante.Modules.Financial.Infrastructure.Accounts;

/// <summary>
/// Movimentos de uma conta num intervalo de datas UTC, com o mesmo corte do
/// saldo à data (<see cref="AccountBalanceQuery"/>): de <c>from 00:00Z</c>
/// inclusivo a <c>to+1 00:00Z</c> exclusivo. Filtros globais do EF tratam
/// soft-delete e tenant.
/// </summary>
public sealed class CreditCardActivityQuery : ICreditCardActivityQuery
{
    private readonly FinancialDbContext _db;

    public CreditCardActivityQuery(FinancialDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CreditCardMovement>> GetMovementsAsync(
        Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var lowerBound = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var exclusiveUpperBound = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await _db.Transactions
            .Where(t => t.AccountId == accountId
                && t.OccurredAt >= lowerBound
                && t.OccurredAt < exclusiveUpperBound)
            .Select(t => new { t.OccurredAt, t.Kind, t.Direction, t.Amount.Amount })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CreditCardMovement(
                DateOnly.FromDateTime(r.OccurredAt.UtcDateTime), r.Kind, r.Direction, r.Amount))
            .ToList();
    }
}
