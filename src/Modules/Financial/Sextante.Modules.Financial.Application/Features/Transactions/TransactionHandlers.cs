using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.PublicApi.Events;
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
        ICategoryRepository categoryRepository,
        ITenantContext tenant,
        ITenantCurrencyResolver currency,
        IExchangeRateService exchangeRates,
        ICurrencyDirectory currencyDirectory,
        IIntegrationEventPublisher events,
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

        var category = await categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken)
            ?? throw new ArgumentException("Categoria não encontrada.", nameof(command));

        var transaction = Transaction.CreateRegular(
            command.AccountId,
            category.Id,
            category.Kind,
            command.OccurredAt,
            new Money(command.Amount, requestedCurrency),
            command.Description,
            command.Tags,
            tenant.TenantId,
            snapshot);

        await repository.AddAsync(transaction, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await events.PublishAsync(
            new TransactionCreatedIntegrationEvent(
                transaction.Id,
                tenant.TenantId.Value,
                transaction.AccountId,
                transaction.CategoryId,
                transaction.Amount.Amount,
                transaction.Amount.Currency,
                transaction.OccurredAt,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToResponse(transaction);
    }

    public static async Task<TransactionResponse?> Handle(
        UpdateTransactionCommand command,
        ITransactionRepository repository,
        ICategoryRepository categoryRepository,
        ITenantContext tenant,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (transaction is null)
        {
            throw new KeyNotFoundException($"Transaction with ID '{command.Id}' not found.");
        }

        // Update preserva a moeda original e o ER frozen — apenas
        // amount/dates/desc/tags são editáveis.
        var category = await categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken)
            ?? throw new ArgumentException("Categoria não encontrada.", nameof(command));

        transaction.Update(
            command.AccountId,
            category.Id,
            category.Kind,
            command.OccurredAt,
            new Money(command.Amount, transaction.Amount.Currency),
            command.Description,
            command.Tags);

        repository.Update(transaction);
        await repository.SaveChangesAsync(cancellationToken);

        await events.PublishAsync(
            new TransactionUpdatedIntegrationEvent(
                transaction.Id,
                tenant.TenantId.Value,
                transaction.AccountId,
                transaction.CategoryId,
                transaction.Amount.Amount,
                transaction.Amount.Currency,
                transaction.OccurredAt,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return ToResponse(transaction);
    }

    public static async Task<bool> Handle(
        ArchiveTransactionCommand command,
        ITransactionRepository repository,
        ITenantContext tenant,
        IIntegrationEventPublisher events,
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

        // Phase 6.5 §0.6 — o progresso dos orçamentos exclui transações
        // apagadas; o evento faz o subscriber recalcular.
        await events.PublishAsync(
            new TransactionUpdatedIntegrationEvent(
                transaction.Id,
                tenant.TenantId.Value,
                transaction.AccountId,
                transaction.CategoryId,
                transaction.Amount.Amount,
                transaction.Amount.Currency,
                transaction.OccurredAt,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return true;
    }

    // Phase 5.5 — bulk recategorize transactions
    public static async Task<RecategorizeTransactionsResponse> Handle(
        RecategorizeTransactionsCommand command,
        ITransactionRepository repository,
        ICategoryRepository categoryRepository,
        ITenantContext tenant,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        var category = await categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken)
            ?? throw new ArgumentException("Categoria não encontrada.", nameof(command));

        var transactions = await repository.GetByIdsAsync(command.Ids, cancellationToken);
        var updated = 0;

        foreach (var tx in transactions)
        {
            tx.SetCategory(category.Id, category.Kind);
            repository.Update(tx);
            updated++;

            await events.PublishAsync(
                new TransactionUpdatedIntegrationEvent(
                    tx.Id,
                    tenant.TenantId.Value,
                    tx.AccountId,
                    tx.CategoryId,
                    tx.Amount.Amount,
                    tx.Amount.Currency,
                    tx.OccurredAt,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return new RecategorizeTransactionsResponse(updated);
    }

    public static async Task<TransactionResponse> Handle(
        GetTransactionByIdQuery query,
        ITransactionRepository repository,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (transaction is null)
            throw new KeyNotFoundException($"Transaction with ID '{query.Id}' not found.");
        return ToResponse(transaction);
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
            Cursor.Decode(query.Cursor),
            ParseKind(query.Kind),
            query.DescriptionContains,
            query.AmountMin,
            query.AmountMax);

        var page = await repository.ListAsync(filter, cancellationToken);
        var nextCursor = page.NextCursor is null ? null : Cursor.Encode(page.NextCursor);
        return new TransactionsPageResponse(
            page.Items.Select(ToResponse).ToList(),
            nextCursor);
    }

    // Phase 6 — export CSV. Sem paginação: o filtro é o mesmo do list,
    // mas o ficheiro leva tudo o que passa.
    public static async Task<ExportTransactionsResponse> Handle(
        ExportTransactionsQuery query,
        ITransactionRepository repository,
        ITenantCurrencyResolver currency,
        CancellationToken cancellationToken)
    {
        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);

        var filter = new TransactionFilter(
            query.DateFrom,
            query.DateTo,
            query.CategoryIds,
            query.AccountIds,
            query.RecurringRuleId,
            DefaultPageSize,
            null,
            ParseKind(query.Kind),
            query.DescriptionContains,
            query.AmountMin,
            query.AmountMax);

        var rows = await repository.ListForExportAsync(filter, cancellationToken);

        var csv = TransactionCsvWriter.Write(rows.Select(r => new TransactionExportRow(
            r.OccurredAt,
            r.AccountName,
            r.CategoryName ?? string.Empty,
            DescribeKind(r.Kind, r.Direction),
            r.Description,
            r.Amount,
            r.Currency,
            r.ExchangeRateToPrimary,
            primaryCurrency,
            DescribeOrigin(r))));

        var fileName = $"transacoes-{DateTimeOffset.UtcNow:yyyyMMdd}.csv";
        return new ExportTransactionsResponse(fileName, csv);
    }

    /// <summary>
    /// A transação não guarda ligação ao lote de importação (não existe
    /// <c>ImportBatchId</c>), logo "Importação" não é distinguível de
    /// "Manual" — só recorrente e regra deixam rasto.
    /// </summary>
    private static string DescribeOrigin(TransactionExportDataRow row)
        => row switch
        {
            { RecurringRuleId: not null } => "Recorrente",
            { CategorizationRuleId: not null } => "Regra",
            _ => "Manual",
        };

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

    private static CategoryKindFilter? ParseKind(string? kind)
        => kind switch
        {
            null or "" => null,
            _ when string.Equals(kind, "Income", StringComparison.OrdinalIgnoreCase)
                => CategoryKindFilter.Income,
            _ when string.Equals(kind, "Expense", StringComparison.OrdinalIgnoreCase)
                => CategoryKindFilter.Expense,
            _ when string.Equals(kind, "Transfer", StringComparison.OrdinalIgnoreCase)
                => CategoryKindFilter.Transfer,
            _ when string.Equals(kind, "Adjustment", StringComparison.OrdinalIgnoreCase)
                => CategoryKindFilter.Adjustment,
            _ => null,
        };

    private static string DescribeKind(TransactionKind kind, TransactionDirection direction)
        => kind switch
        {
            TransactionKind.Transfer => "Transferência",
            TransactionKind.Adjustment => "Acerto",
            _ => direction == TransactionDirection.Inflow ? "Receita" : "Despesa",
        };

    private static string NormalizeViewMode(string? mode)
        => string.Equals(mode, ViewModeOriginal, StringComparison.OrdinalIgnoreCase)
            ? ViewModeOriginal
            : ViewModeConverted;

    // internal (não private) — Phase 6.5 grupo 3: TransferHandlers reaproveita
    // este mapeamento para cada perna, mesmo assembly.
    internal static TransactionResponse ToResponse(Transaction transaction)
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
            transaction.UpdatedAt,
            transaction.Direction,
            transaction.Kind,
            transaction.TransferId);
}
