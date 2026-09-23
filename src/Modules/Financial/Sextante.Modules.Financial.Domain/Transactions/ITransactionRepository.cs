using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Transactions;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Transaction>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken);
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);
    void Update(Transaction transaction);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<TransactionPage> ListAsync(TransactionFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Soma converted (em <paramref name="primaryCurrency"/>) usando
    /// <c>COALESCE(ExchangeRateToPrimary, 1.0)</c>. NULL é "1.0
    /// implied" para rows pré-Phase-3 ou onde currency==primary.
    /// </summary>
    Task<TransactionTotals> GetConvertedTotalsAsync(
        TransactionFilter filter,
        string primaryCurrency,
        CancellationToken cancellationToken);

    /// <summary>
    /// Devolve totais agrupados pela moeda original da transação.
    /// </summary>
    Task<IReadOnlyList<TransactionTotalsByCurrencyRow>> GetTotalsByCurrencyAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken);

    /// <summary>
    /// Phase 6 — linhas desnormalizadas (conta e categoria resolvidas) para
    /// o export CSV. Sem paginação: aplica o filtro e devolve tudo o que
    /// passa, ordenado da mais recente para a mais antiga.
    /// </summary>
    Task<IReadOnlyList<TransactionExportDataRow>> ListForExportAsync(
        TransactionFilter filter,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TransactionByCategoryRow>> GetByCategoryAsync(
        TransactionFilter filter,
        CategoryKindFilter kindFilter,
        string primaryCurrency,
        CancellationToken cancellationToken);
}

public sealed record TransactionFilter(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyCollection<Guid>? CategoryIds,
    IReadOnlyCollection<Guid>? AccountIds,
    Guid? RecurringRuleId,
    int PageSize,
    TransactionCursor? Cursor,
    // Phase 6 — filtros antes aplicados client-side no /transactions.
    CategoryKindFilter? Kind = null,
    string? DescriptionContains = null,
    decimal? AmountMin = null,
    decimal? AmountMax = null);

public sealed record TransactionCursor(DateTimeOffset OccurredAt, Guid Id);

public sealed record TransactionPage(IReadOnlyList<Transaction> Items, TransactionCursor? NextCursor);

public sealed record TransactionTotals(Money Income, Money Expense, Money Net);

public sealed record TransactionTotalsByCurrencyRow(
    string Currency,
    decimal Income,
    decimal Expense);

public sealed record TransactionExportDataRow(
    DateTimeOffset OccurredAt,
    string AccountName,
    string? CategoryName,
    TransactionKind Kind,
    TransactionDirection Direction,
    string? Description,
    decimal Amount,
    string Currency,
    decimal? ExchangeRateToPrimary,
    Guid? CategorizationRuleId,
    Guid? RecurringRuleId);

public sealed record TransactionByCategoryRow(
    Guid CategoryId,
    string CategoryName,
    string IconName,
    string ColorHex,
    Money Total);

/// <summary>
/// Filtro de tipo da listagem. <c>Expense</c>/<c>Income</c> = transações
/// regulares de saída/entrada; <c>Transfer</c>/<c>Adjustment</c> = pelo
/// <see cref="TransactionKind"/> (Phase 6.5).
/// </summary>
public enum CategoryKindFilter
{
    Expense = 0,
    Income = 1,
    Transfer = 2,
    Adjustment = 3,
}
