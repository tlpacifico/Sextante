using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.Infrastructure.Persistence;

namespace Sextante.Modules.Financial.Infrastructure.Transfers;

public sealed class TransferCounterpartQuery : ITransferCounterpartQuery
{
    private readonly FinancialDbContext _db;

    public TransferCounterpartQuery(FinancialDbContext db) => _db = db;

    public async Task<IReadOnlyList<TransferCandidate>> FindAsync(
        Guid accountId,
        TransactionKind kind,
        TransactionDirection direction,
        decimal amount,
        string currency,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(from, TimeOnly.MinValue, TimeSpan.Zero);
        var end = new DateTimeOffset(to.AddDays(1), TimeOnly.MinValue, TimeSpan.Zero);

        var matching = _db.Transactions.Where(t =>
            t.AccountId == accountId
            && t.Kind == kind
            && t.Direction == direction
            && t.Amount.Amount == amount
            && t.Amount.Currency == currency
            && t.OccurredAt >= start
            && t.OccurredAt < end);

        if (kind == TransactionKind.Regular)
        {
            var regular = await matching
                .Where(t => t.TransferId == null)
                .Select(t => new { t.Id, t.OccurredAt })
                .ToListAsync(cancellationToken);

            return regular
                .Select(t => new TransferCandidate(t.Id, DateOnly.FromDateTime(t.OccurredAt.UtcDateTime), null))
                .ToList();
        }

        var legs = await (
            from t1 in matching
            join t2 in _db.Transactions on t1.TransferId equals t2.TransferId
            where t1.Id != t2.Id
            select new { t1.Id, t1.OccurredAt, CounterpartAccountId = t2.AccountId })
            .ToListAsync(cancellationToken);

        return legs
            .Select(t => new TransferCandidate(
                t.Id, DateOnly.FromDateTime(t.OccurredAt.UtcDateTime), t.CounterpartAccountId))
            .ToList();
    }
}
