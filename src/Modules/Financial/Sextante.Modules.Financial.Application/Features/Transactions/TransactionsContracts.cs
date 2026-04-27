using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.Transactions;

public sealed record TransactionResponse(
    Guid Id,
    Guid AccountId,
    Guid CategoryId,
    DateTimeOffset OccurredAt,
    Money Amount,
    string? Description,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateTransactionCommand(
    Guid AccountId,
    Guid CategoryId,
    DateTimeOffset OccurredAt,
    decimal Amount,
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

public sealed record GetTransactionByIdQuery(Guid Id);

public sealed record ListTransactionsQuery(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? AccountIds,
    int? PageSize,
    string? Cursor);

public sealed record TransactionsPageResponse(
    IReadOnlyList<TransactionResponse> Items,
    string? NextCursor);

public sealed record TransactionSummaryQuery(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? AccountIds);

public sealed record TransactionSummaryResponse(Money Income, Money Expense, Money Net);

public sealed record TransactionsByCategoryQuery(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    IReadOnlyList<Guid>? CategoryIds,
    IReadOnlyList<Guid>? AccountIds,
    string Kind);

public sealed record TransactionByCategoryResponse(
    Guid CategoryId,
    string CategoryName,
    string IconName,
    string ColorHex,
    Money Total);
