using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.Transactions;

[NonTransactional]
public static class TransactionHandlers
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public static async Task<TransactionResponse> Handle(
        CreateTransactionCommand command,
        ITransactionRepository repository,
        ITenantContext tenant,
        ITenantCurrencyResolver currency,
        CancellationToken cancellationToken)
    {
        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        var transaction = Transaction.Create(
            command.AccountId,
            command.CategoryId,
            command.OccurredAt,
            new Money(command.Amount, primaryCurrency),
            command.Description,
            command.Tags,
            tenant.TenantId);

        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return ToResponse(transaction);
    }

    public static async Task<TransactionResponse?> Handle(
        UpdateTransactionCommand command,
        ITransactionRepository repository,
        ITenantCurrencyResolver currency,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        transaction.Update(
            command.AccountId,
            command.CategoryId,
            command.OccurredAt,
            new Money(command.Amount, primaryCurrency),
            command.Description,
            command.Tags);

        repository.Update(transaction);
        await repository.SaveChangesAsync(cancellationToken);
        return ToResponse(transaction);
    }

    public static async Task<bool> Handle(
        ArchiveTransactionCommand command,
        ITransactionRepository repository,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (transaction is null)
        {
            return false;
        }

        transaction.Archive();
        repository.Update(transaction);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static async Task<TransactionResponse?> Handle(
        GetTransactionByIdQuery query,
        ITransactionRepository repository,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(query.Id, cancellationToken);
        return transaction is null ? null : ToResponse(transaction);
    }

    public static async Task<TransactionsPageResponse> Handle(
        ListTransactionsQuery query,
        ITransactionRepository repository,
        CancellationToken cancellationToken)
    {
        var pageSize = query.PageSize is { } size
            ? Math.Clamp(size, 1, MaxPageSize)
            : DefaultPageSize;

        var filter = new TransactionFilter(
            query.DateFrom,
            query.DateTo,
            query.CategoryIds,
            query.AccountIds,
            pageSize,
            Cursor.Decode(query.Cursor));

        var page = await repository.ListAsync(filter, cancellationToken);
        var nextCursor = page.NextCursor is null ? null : Cursor.Encode(page.NextCursor);
        return new TransactionsPageResponse(
            page.Items.Select(ToResponse).ToList(),
            nextCursor);
    }

    public static async Task<TransactionSummaryResponse> Handle(
        TransactionSummaryQuery query,
        ITransactionRepository repository,
        CancellationToken cancellationToken)
    {
        var filter = new TransactionFilter(
            query.DateFrom,
            query.DateTo,
            query.CategoryIds,
            query.AccountIds,
            DefaultPageSize,
            null);

        var totals = await repository.GetTotalsAsync(filter, cancellationToken);
        return new TransactionSummaryResponse(totals.Income, totals.Expense, totals.Net);
    }

    public static async Task<IReadOnlyList<TransactionByCategoryResponse>> Handle(
        TransactionsByCategoryQuery query,
        ITransactionRepository repository,
        CancellationToken cancellationToken)
    {
        var kindFilter = string.Equals(query.Kind, "Income", StringComparison.OrdinalIgnoreCase)
            ? CategoryKindFilter.Income
            : CategoryKindFilter.Expense;

        var filter = new TransactionFilter(
            query.DateFrom,
            query.DateTo,
            query.CategoryIds,
            query.AccountIds,
            DefaultPageSize,
            null);

        var rows = await repository.GetByCategoryAsync(filter, kindFilter, cancellationToken);
        return rows
            .Select(r => new TransactionByCategoryResponse(
                r.CategoryId,
                r.CategoryName,
                r.IconName,
                r.ColorHex,
                r.Total))
            .ToList();
    }

    private static TransactionResponse ToResponse(Transaction transaction)
        => new(
            transaction.Id,
            transaction.AccountId,
            transaction.CategoryId,
            transaction.OccurredAt,
            transaction.Amount,
            transaction.Description,
            transaction.Tags.ToList(),
            transaction.CreatedAt,
            transaction.UpdatedAt);
}
