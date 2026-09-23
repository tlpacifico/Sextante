using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.Transactions;

public sealed record TransactionResponse(
    Guid Id,
    Guid AccountId,
    Guid? CategoryId,
    DateTimeOffset OccurredAt,
    Money Amount,
    string? Description,
    IReadOnlyList<string> Tags,
    decimal? ExchangeRateToPrimary,
    DateTimeOffset? ExchangeRateAt,
    Guid? RecurringRuleId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    TransactionDirection Direction,
    TransactionKind Kind,
    Guid? TransferId);

public sealed record CreateTransactionCommand(
    Guid AccountId,
    Guid CategoryId,
    DateTimeOffset OccurredAt,
    decimal Amount,
    string? Currency,
    string? Description,
    IReadOnlyList<string>? Tags);

public sealed record UpdateTransactionCommand(
    Guid Id,
    Guid AccountId,
    Guid CategoryId,
    DateTimeOffset OccurredAt,
    decimal Amount,
    string? Description,
    IReadOnlyList<string>? Tags);

public sealed record ArchiveTransactionCommand(Guid Id);

public sealed record RecategorizeTransactionsCommand(
    IReadOnlyList<Guid> Ids,
    Guid CategoryId);

public sealed record RecategorizeTransactionsResponse(
    int UpdatedCount);

public sealed record GetTransactionByIdQuery(Guid Id);

public sealed record ListTransactionsQuery(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? AccountIds,
    Guid? RecurringRuleId,
    int? PageSize,
    string? Cursor,
    // Phase 6 — antes filtrados client-side no /transactions, logo só
    // dentro da página corrente.
    string? Kind = null,
    string? DescriptionContains = null,
    decimal? AmountMin = null,
    decimal? AmountMax = null);

public sealed record TransactionsPageResponse(
    IReadOnlyList<TransactionResponse> Items,
    string? NextCursor);

/// <summary>
/// Phase 6 — export CSV. Mesmos filtros do <see cref="ListTransactionsQuery"/>,
/// sem paginação.
/// </summary>
public sealed record ExportTransactionsQuery(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? AccountIds,
    Guid? RecurringRuleId,
    string? Kind = null,
    string? DescriptionContains = null,
    decimal? AmountMin = null,
    decimal? AmountMax = null);

/// <summary>
/// Linha já desnormalizada (nomes de conta e categoria resolvidos) pronta
/// a escrever no CSV.
/// </summary>
public sealed record TransactionExportRow(
    DateTimeOffset OccurredAt,
    string AccountName,
    string CategoryName,
    string Kind,
    string? Description,
    decimal Amount,
    string Currency,
    decimal? ExchangeRateToPrimary,
    string PrimaryCurrency,
    string Origin);

public sealed record ExportTransactionsResponse(
    string FileName,
    string Csv);

public sealed record TransactionSummaryQuery(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? AccountIds,
    string? ViewMode);

public sealed record TransactionSummaryResponse(
    Money Income,
    Money Expense,
    Money Net,
    string ViewMode,
    IReadOnlyList<CurrencyTotals>? PerCurrency);

public sealed record CurrencyTotals(
    string Currency,
    Money Income,
    Money Expense,
    Money Net);

public sealed record TransactionsByCategoryQuery(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? AccountIds,
    string Kind,
    string? ViewMode);

public sealed record TransactionByCategoryResponse(
    Guid CategoryId,
    string CategoryName,
    string IconName,
    string ColorHex,
    Money Total);
