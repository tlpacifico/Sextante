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

    public async Task<IReadOnlyList<Transaction>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
        => await _db.Transactions.Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken);

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

        // Phase 6.5 (ADR-014) — só transações regulares são receita/despesa;
        // a direção é da transação, sem depender da categoria (as sem
        // categoria também contam).
        var rows = await query
            .Where(t => t.Kind == TransactionKind.Regular)
            .Select(t => new
            {
                Amount = t.Amount.Amount,
                ExchangeRate = t.ExchangeRateToPrimary ?? 1.0m,
                t.Direction,
            })
            .ToListAsync(cancellationToken);

        var income = rows.Where(r => r.Direction == TransactionDirection.Inflow).Sum(r => r.Amount * r.ExchangeRate);
        var expense = rows.Where(r => r.Direction == TransactionDirection.Outflow).Sum(r => r.Amount * r.ExchangeRate);

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
            .Where(t => t.Kind == TransactionKind.Regular)
            .Select(t => new
            {
                Amount = t.Amount.Amount,
                Currency = t.Amount.Currency,
                t.Direction,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.Currency, StringComparer.Ordinal)
            .Select(g => new TransactionTotalsByCurrencyRow(
                g.Key,
                g.Where(x => x.Direction == TransactionDirection.Inflow).Sum(x => x.Amount),
                g.Where(x => x.Direction == TransactionDirection.Outflow).Sum(x => x.Amount)))
            .OrderBy(r => r.Currency, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<TransactionExportDataRow>> ListForExportAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken)
    {
        var query = ApplyFilter(_db.Transactions.AsQueryable(), filter);

        // Join a Accounts e Categories para o CSV levar nomes em vez de
        // GUIDs. Global Query Filter + RLS continuam a aplicar-se aos três.
        // A ordenação tem de vir antes da projecção para o record: o EF não
        // traduz OrderBy sobre uma propriedade de um objecto construído.
        var rows = await query
            .Join(
                _db.Accounts,
                t => t.AccountId,
                a => a.Id,
                (t, a) => new { Transaction = t, AccountName = a.Name })
            // Left join: transferências, acertos e transações sem categoria
            // também vão para o ficheiro (Phase 6.5).
            .GroupJoin(
                CategoriesIncludingArchived,
                x => new { Id = x.Transaction.CategoryId, x.Transaction.TenantId },
                c => new { Id = (Guid?)c.Id, c.TenantId },
                (x, categories) => new { x.Transaction, x.AccountName, Categories = categories })
            .SelectMany(
                x => x.Categories.DefaultIfEmpty(),
                (x, c) => new { x.Transaction, x.AccountName, CategoryName = c == null ? null : c.Name })
            .OrderByDescending(x => x.Transaction.OccurredAt)
            .ThenByDescending(x => x.Transaction.Id)
            .ToListAsync(cancellationToken);

        // Grupo 3 — conta contraparte das transferências: reaproveita
        // GetCounterpartAccountIdsAsync (1 query, sem N+1) em vez de mais um
        // join na projeção acima; o export não é paginado, por isso duas
        // idas extra à BD (contraparte + nomes) são aceitáveis.
        var transferIds = rows
            .Where(x => x.Transaction.Kind == TransactionKind.Transfer)
            .Select(x => x.Transaction.Id)
            .ToList();
        var counterpartAccountIds = transferIds.Count > 0
            ? await GetCounterpartAccountIdsAsync(transferIds, cancellationToken)
            : new Dictionary<Guid, Guid>();
        var counterpartAccountNames = counterpartAccountIds.Count > 0
            ? await _db.Accounts
                .Where(a => counterpartAccountIds.Values.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        return rows
            .Select(x => new TransactionExportDataRow(
                x.Transaction.OccurredAt,
                x.AccountName,
                x.CategoryName,
                x.Transaction.Kind,
                x.Transaction.Direction,
                x.Transaction.Description,
                x.Transaction.Amount.Amount,
                x.Transaction.Amount.Currency,
                x.Transaction.ExchangeRateToPrimary,
                x.Transaction.CategorizationRuleId,
                x.Transaction.RecurringRuleId,
                counterpartAccountIds.TryGetValue(x.Transaction.Id, out var counterpartAccountId)
                    && counterpartAccountNames.TryGetValue(counterpartAccountId, out var counterpartAccountName)
                    ? counterpartAccountName
                    : null))
            .ToList();
    }

    public async Task<IReadOnlyList<TransactionByCategoryRow>> GetByCategoryAsync(
        TransactionFilter filter,
        CategoryKindFilter kindFilter,
        string primaryCurrency,
        CancellationToken cancellationToken)
    {
        var direction = kindFilter == CategoryKindFilter.Income
            ? TransactionDirection.Inflow
            : TransactionDirection.Outflow;

        var query = ApplyFilter(_db.Transactions.AsQueryable(), filter)
            .Where(t => t.Kind == TransactionKind.Regular && t.Direction == direction);

        // Transações sem categoria não têm fatia no donut (ficam só no summary).
        var rows = await query
            .Join(
                CategoriesIncludingArchived,
                t => new { Id = t.CategoryId, t.TenantId },
                c => new { Id = (Guid?)c.Id, c.TenantId },
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

    public async Task<IReadOnlyList<Transaction>> GetByTransferIdAsync(Guid transferId, CancellationToken cancellationToken)
        => await _db.Transactions.Where(t => t.TransferId == transferId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetCounterpartAccountIdsAsync(
        IReadOnlyCollection<Guid> transactionIds, CancellationToken cancellationToken)
    {
        var pairs = await (
            from t1 in _db.Transactions
            join t2 in _db.Transactions on t1.TransferId equals t2.TransferId
            where transactionIds.Contains(t1.Id) && t1.Id != t2.Id
            select new { t1.Id, CounterpartAccountId = t2.AccountId })
            .ToListAsync(cancellationToken);

        return pairs.ToDictionary(p => p.Id, p => p.CounterpartAccountId);
    }

    // Phase 6.5 §0.4 — categorias arquivadas (soft-delete) continuam a
    // nomear e classificar as transações antigas. IgnoreQueryFilters tira
    // também o filtro de tenant, por isso todos os joins são por
    // (Id, TenantId) com a transação, que já vem filtrada por tenant; a
    // RLS da DB é a segunda barreira.
    private IQueryable<Category> CategoriesIncludingArchived
        => _db.Categories.IgnoreQueryFilters();

    // Instance (não static) porque o filtro de Kind precisa de
    // _db.Categories — o kind vive na categoria, não na transação.
    private IQueryable<Transaction> ApplyFilter(
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
            query = query.Where(t => t.CategoryId != null && categoryIds.Contains(t.CategoryId.Value));
        }

        if (filter.AccountIds is { Count: > 0 } accountIds)
        {
            query = query.Where(t => accountIds.Contains(t.AccountId));
        }

        if (filter.RecurringRuleId is { } recurringRuleId)
        {
            query = query.Where(t => t.RecurringRuleId == recurringRuleId);
        }

        if (filter.Kind is { } kindFilter)
        {
            query = kindFilter switch
            {
                CategoryKindFilter.Income => query.Where(t =>
                    t.Kind == TransactionKind.Regular && t.Direction == TransactionDirection.Inflow),
                CategoryKindFilter.Expense => query.Where(t =>
                    t.Kind == TransactionKind.Regular && t.Direction == TransactionDirection.Outflow),
                CategoryKindFilter.Transfer => query.Where(t => t.Kind == TransactionKind.Transfer),
                CategoryKindFilter.Adjustment => query.Where(t => t.Kind == TransactionKind.Adjustment),
                _ => query,
            };
        }

        if (!string.IsNullOrWhiteSpace(filter.DescriptionContains))
        {
            // Escapar wildcards do LIKE para que o texto do utilizador
            // seja tratado como literal.
            var term = filter.DescriptionContains
                .Trim()
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);

            query = query.Where(t =>
                t.Description != null
                && EF.Functions.ILike(t.Description, $"%{term}%", "\\"));
        }

        if (filter.AmountMin is { } amountMin)
        {
            query = query.Where(t => t.Amount.Amount >= amountMin);
        }

        if (filter.AmountMax is { } amountMax)
        {
            query = query.Where(t => t.Amount.Amount <= amountMax);
        }

        return query;
    }
}
