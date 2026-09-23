using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Application.Features.Accounts;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.Infrastructure.Persistence;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Infrastructure.Accounts;

/// <summary>
/// Calcula saldos on-demand: <c>OpeningBalance + Σ SignedAmount</c> sobre
/// <c>financial.transactions</c> desde <c>OpeningBalanceDate</c>, por todos
/// os tipos (Regular, Transfer, Adjustment). Em <see cref="GetCurrentBalancesAsync"/>,
/// para evitar N+1, carrega todas as contas do tenant numa query e todas as
/// transações relevantes noutra — o corte por conta (cada conta tem a sua
/// própria <c>OpeningBalanceDate</c>) e o agrupamento por <c>AccountId</c>
/// fazem-se em memória sobre as linhas já materializadas (poucas linhas,
/// uso pessoal — mesma técnica de <c>BudgetProgressService</c>).
/// </summary>
public sealed class AccountBalanceQuery : IAccountBalanceQuery
{
    private readonly FinancialDbContext _db;

    public AccountBalanceQuery(FinancialDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<Guid, Money>> GetCurrentBalancesAsync(CancellationToken cancellationToken)
    {
        var accounts = await _db.Accounts
            .Select(a => new AccountRow(a.Id, a.Currency, a.OpeningBalance.Amount, a.OpeningBalanceDate))
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return new Dictionary<Guid, Money>();
        }

        // Uma única query cobre todas as contas: usa a data mais antiga
        // entre elas como corte inferior; o corte exato por conta é
        // aplicado em memória a seguir.
        var minOpeningBalanceDateTime = accounts
            .Min(a => a.OpeningBalanceDate)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var rows = await _db.Transactions
            .Where(t => t.OccurredAt >= minOpeningBalanceDateTime)
            .Select(t => new TxRow(t.AccountId, t.Direction, t.Amount.Amount, t.OccurredAt))
            .ToListAsync(cancellationToken);

        var rowsByAccount = rows.ToLookup(r => r.AccountId);

        var balances = new Dictionary<Guid, Money>(accounts.Count);
        foreach (var account in accounts)
        {
            var openingBalanceDateTime = account.OpeningBalanceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            var sum = rowsByAccount[account.Id]
                .Where(r => r.OccurredAt >= openingBalanceDateTime)
                .Sum(r => SignedAmount(r.Direction, r.Amount));

            balances[account.Id] = new Money(account.OpeningBalanceAmount + sum, account.Currency);
        }

        return balances;
    }

    public async Task<Money?> GetBalanceAsync(Guid accountId, DateOnly? at, CancellationToken cancellationToken)
    {
        var account = await _db.Accounts
            .Where(a => a.Id == accountId)
            .Select(a => new AccountRow(a.Id, a.Currency, a.OpeningBalance.Amount, a.OpeningBalanceDate))
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
        {
            return null;
        }

        var openingBalanceDateTime = account.OpeningBalanceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var query = _db.Transactions
            .Where(t => t.AccountId == accountId && t.OccurredAt >= openingBalanceDateTime);

        if (at is { } cutoff)
        {
            // Corte inclusivo do dia: < início do dia seguinte.
            var exclusiveUpperBound = cutoff.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(t => t.OccurredAt < exclusiveUpperBound);
        }

        var sum = await query
            .Select(t => t.Direction == TransactionDirection.Inflow ? t.Amount.Amount : -t.Amount.Amount)
            .SumAsync(cancellationToken);

        return new Money(account.OpeningBalanceAmount + sum, account.Currency);
    }

    private static decimal SignedAmount(TransactionDirection direction, decimal amount)
        => direction == TransactionDirection.Inflow ? amount : -amount;

    private sealed record AccountRow(Guid Id, string Currency, decimal OpeningBalanceAmount, DateOnly OpeningBalanceDate);

    private sealed record TxRow(Guid AccountId, TransactionDirection Direction, decimal Amount, DateTimeOffset OccurredAt);
}
