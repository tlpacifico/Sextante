using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class TransactionRepository : ITransactionRepository
{
    private readonly FinancialDbContext _db;

    public TransactionRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.Transactions.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
        => _db.Transactions.AddAsync(transaction, cancellationToken).AsTask();

    public void Update(Transaction transaction) => _db.Transactions.Update(transaction);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);

    public async Task<TransactionPage> ListAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilter(_db.Transactions.AsQueryable(), filter);

        if (filter.Cursor is not null)
        {
            var cursor = filter.Cursor;
            query = query.Where(t =>
                t.OccurredAt < cursor.OccurredAt
                || (t.OccurredAt == cursor.OccurredAt && t.Id.CompareTo(cursor.Id) < 0));
        }

        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(t => t.OccurredAt)
            .ThenByDescending(t => t.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        TransactionCursor? nextCursor = null;
        if (items.Count > pageSize)
        {
            var last = items[pageSize - 1];
            nextCursor = new TransactionCursor(last.OccurredAt, last.Id);
            items = items.Take(pageSize).ToList();
        }

        return new TransactionPage(items, nextCursor);
    }

    public async Task<TransactionTotals> GetConvertedTotalsAsync(
        TransactionFilter filter,
        string primaryCurrency,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilter(_db.Transactions.AsQueryable(), filter);

        var rows = await query
            .Join(
                _db.Categories,
                t => t.CategoryId,
                c => c.Id,
                (t, c) => new
                {
                    Amount = t.Amount.Amount,
                    ExchangeRate = t.ExchangeRateToPrimary ?? 1.0m,
                    c.Kind,
                })
            .ToListAsync(cancellationToken);

        var income = rows.Where(r => r.Kind == CategoryKind.Income).Sum(r => r.Amount * r.ExchangeRate);
        var expense = rows.Where(r => r.Kind == CategoryKind.Expense).Sum(r => r.Amount * r.ExchangeRate);

        return new TransactionTotals(
            new Money(income, primaryCurrency),
            new Money(expense, primaryCurrency),
            new Money(income - expense, primaryCurrency));
    }

    public async Task<IReadOnlyList<TransactionTotalsByCurrencyRow>> GetTotalsByCurrencyAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilter(_db.Transactions.AsQueryable(), filter);

        var rows = await query
            .Join(
                _db.Categories,
                t => t.CategoryId,
                c => c.Id,
                (t, c) => new
                {
                    Amount = t.Amount.Amount,
                    Currency = t.Amount.Currency,
                    c.Kind,
                })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.Currency, StringComparer.Ordinal)
            .Select(g => new TransactionTotalsByCurrencyRow(
                g.Key,
                g.Where(x => x.Kind == CategoryKind.Income).Sum(x => x.Amount),
                g.Where(x => x.Kind == CategoryKind.Expense).Sum(x => x.Amount)))
            .OrderBy(r => r.Currency, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<TransactionByCategoryRow>> GetByCategoryAsync(
        TransactionFilter filter,
        CategoryKindFilter kindFilter,
        string primaryCurrency,
        CancellationToken cancellationToken)
    {
        var kind = kindFilter == CategoryKindFilter.Income ? CategoryKind.Income : CategoryKind.Expense;

        var query = ApplyFilter(_db.Transactions.AsQueryable(), filter);

        var rows = await query
            .Join(
                _db.Categories.Where(c => c.Kind == kind),
                t => t.CategoryId,
                c => c.Id,
                (t, c) => new
                {
                    c.Id,
                    c.Name,
                    c.IconName,
                    c.ColorHex,
                    Amount = t.Amount.Amount,
                    ExchangeRate = t.ExchangeRateToPrimary ?? 1.0m,
                })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => new { x.Id, x.Name, x.IconName, x.ColorHex })
            .Select(g =>
            {
                var total = g.Sum(x => x.Amount * x.ExchangeRate);
                return new TransactionByCategoryRow(
                    g.Key.Id,
                    g.Key.Name,
                    g.Key.IconName,
                    g.Key.ColorHex,
                    new Money(total, primaryCurrency));
            })
            .OrderByDescending(r => r.Total.Amount)
            .ToList();
    }

    private static IQueryable<Transaction> ApplyFilter(
        IQueryable<Transaction> query,
        TransactionFilter filter)
    {
        if (filter.DateFrom is { } from)
        {
            query = query.Where(t => t.OccurredAt >= from);
        }

        if (filter.DateTo is { } to)
        {
            query = query.Where(t => t.OccurredAt <= to);
        }

        if (filter.CategoryIds is { Count: > 0 } categoryIds)
        {
            query = query.Where(t => categoryIds.Contains(t.CategoryId));
        }

        if (filter.AccountIds is { Count: > 0 } accountIds)
        {
            query = query.Where(t => accountIds.Contains(t.AccountId));
        }

        return query;
    }
}
