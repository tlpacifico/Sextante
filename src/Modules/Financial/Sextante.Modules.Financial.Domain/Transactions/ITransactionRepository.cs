using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Transactions;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);
    void Update(Transaction transaction);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<TransactionPage> ListAsync(TransactionFilter filter, CancellationToken cancellationToken);
    Task<TransactionTotals> GetTotalsAsync(TransactionFilter filter, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionByCategoryRow>> GetByCategoryAsync(
        TransactionFilter filter,
        CategoryKindFilter kindFilter,
        CancellationToken cancellationToken);
}

public sealed record TransactionFilter(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyCollection<Guid>? CategoryIds,
    IReadOnlyCollection<Guid>? AccountIds,
    int PageSize,
    TransactionCursor? Cursor);

public sealed record TransactionCursor(DateTimeOffset OccurredAt, Guid Id);

public sealed record TransactionPage(IReadOnlyList<Transaction> Items, TransactionCursor? NextCursor);

public sealed record TransactionTotals(Money Income, Money Expense, Money Net);

public sealed record TransactionByCategoryRow(
    Guid CategoryId,
    string CategoryName,
    string IconName,
    string ColorHex,
    Money Total);

public enum CategoryKindFilter
{
    Expense = 0,
    Income = 1,
}
