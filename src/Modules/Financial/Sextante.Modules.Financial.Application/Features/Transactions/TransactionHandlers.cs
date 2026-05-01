using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
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

    public const string ViewModeConverted = "converted";
    public const string ViewModeOriginal = "original";

    public static async Task<TransactionResponse> Handle(
        CreateTransactionCommand command,
        ITransactionRepository repository,
        IAccountRepository accountRepository,
        ITenantContext tenant,
        ITenantCurrencyResolver currency,
        IExchangeRateService exchangeRates,
        ICurrencyDirectory currencyDirectory,
        CancellationToken cancellationToken)
    {
        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);

        // Por default, currency da transação == currency da conta;
        // o frontend prefilla. Se vier override, valida contra
        // allowlist ativa.
        var account = await accountRepository.GetByIdAsync(command.AccountId, cancellationToken)
            ?? throw new ArgumentException("Conta não encontrada.", nameof(command));

        var requestedCurrency = string.IsNullOrWhiteSpace(command.Currency)
            ? account.Currency
            : command.Currency.Trim().ToUpperInvariant();

        if (!await currencyDirectory.IsActiveAsync(requestedCurrency, cancellationToken))
        {
            throw new CurrencyNotActiveException(requestedCurrency);
        }

        var snapshot = await exchangeRates.ResolveAsync(
            requestedCurrency,
            primaryCurrency,
            command.OccurredAt,
            cancellationToken);

        var transaction = Transaction.Create(
            command.AccountId,
            command.CategoryId,
            command.OccurredAt,
            new Money(command.Amount, requestedCurrency),
            command.Description,
            command.Tags,
            tenant.TenantId,
            snapshot);

        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return ToResponse(transaction);
    }

    public static async Task<TransactionResponse?> Handle(
        UpdateTransactionCommand command,
        ITransactionRepository repository,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        // Update preserva a moeda original e o ER frozen — apenas
        // amount/dates/desc/tags são editáveis.
        transaction.Update(
            command.AccountId,
            command.CategoryId,
            command.OccurredAt,
            new Money(command.Amount, transaction.Amount.Currency),
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
            query.RecurringRuleId,
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
        ITenantCurrencyResolver currency,
        CancellationToken cancellationToken)
    {
        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        var viewMode = NormalizeViewMode(query.ViewMode);

        var filter = new TransactionFilter(
            query.DateFrom,
            query.DateTo,
            query.CategoryIds,
            query.AccountIds,
            null,
            DefaultPageSize,
            null);

        if (viewMode == ViewModeOriginal)
        {
            var rows = await repository.GetTotalsByCurrencyAsync(filter, cancellationToken);
            var perCurrency = rows
                .Select(r => new CurrencyTotals(
                    r.Currency,
                    new Money(r.Income, r.Currency),
                    new Money(r.Expense, r.Currency),
                    new Money(r.Income - r.Expense, r.Currency)))
                .ToList();

            return new TransactionSummaryResponse(
                new Money(0m, primaryCurrency),
                new Money(0m, primaryCurrency),
                new Money(0m, primaryCurrency),
                ViewModeOriginal,
                perCurrency);
        }

        var totals = await repository.GetConvertedTotalsAsync(filter, primaryCurrency, cancellationToken);
        return new TransactionSummaryResponse(
            totals.Income,
            totals.Expense,
            totals.Net,
            ViewModeConverted,
            null);
    }

    public static async Task<IReadOnlyList<TransactionByCategoryResponse>> Handle(
        TransactionsByCategoryQuery query,
        ITransactionRepository repository,
        ITenantCurrencyResolver currency,
        CancellationToken cancellationToken)
    {
        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        var viewMode = NormalizeViewMode(query.ViewMode);

        // Original mode esconde o donut no frontend; backend devolve
        // lista vazia para evitar mostrar somas multi-moeda misleading.
        if (viewMode == ViewModeOriginal)
        {
            return Array.Empty<TransactionByCategoryResponse>();
        }

        var kindFilter = string.Equals(query.Kind, "Income", StringComparison.OrdinalIgnoreCase)
            ? CategoryKindFilter.Income
            : CategoryKindFilter.Expense;

        var filter = new TransactionFilter(
            query.DateFrom,
            query.DateTo,
            query.CategoryIds,
            query.AccountIds,
            null,
            DefaultPageSize,
            null);

        var rows = await repository.GetByCategoryAsync(filter, kindFilter, primaryCurrency, cancellationToken);
        return rows
            .Select(r => new TransactionByCategoryResponse(
                r.CategoryId,
                r.CategoryName,
                r.IconName,
                r.ColorHex,
                r.Total))
            .ToList();
    }

    private static string NormalizeViewMode(string? mode)
        => string.Equals(mode, ViewModeOriginal, StringComparison.OrdinalIgnoreCase)
            ? ViewModeOriginal
            : ViewModeConverted;

    private static TransactionResponse ToResponse(Transaction transaction)
        => new(
            transaction.Id,
            transaction.AccountId,
            transaction.CategoryId,
            transaction.OccurredAt,
            transaction.Amount,
            transaction.Description,
            transaction.Tags.ToList(),
            transaction.ExchangeRateToPrimary,
            transaction.ExchangeRateAt,
            transaction.RecurringRuleId,
            transaction.CreatedAt,
            transaction.UpdatedAt);
}
