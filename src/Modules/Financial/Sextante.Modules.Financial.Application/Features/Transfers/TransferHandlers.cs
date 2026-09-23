using Sextante.Infrastructure.ErrorHandling;
using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Application.Features.Transactions;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.PublicApi.Events;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.Transfers;

[NonTransactional]
public static class TransferHandlers
{
    public static async Task<TransferResponse> Handle(
        CreateTransferCommand command,
        ITransactionRepository repository,
        IAccountRepository accountRepository,
        ITenantContext tenant,
        ITenantCurrencyResolver currency,
        IExchangeRateService exchangeRates,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        var (fromAccount, toAccount, amountIn) = await ValidateAccountsAndAmountInAsync(
            command.FromAccountId,
            command.ToAccountId,
            command.AmountOut,
            command.AmountIn,
            accountRepository,
            cancellationToken);

        var transferId = GuidV7.NewId();

        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        var outSnapshot = await exchangeRates.ResolveAsync(
            fromAccount.Currency, primaryCurrency, command.OccurredAt, cancellationToken);
        var inSnapshot = await exchangeRates.ResolveAsync(
            toAccount.Currency, primaryCurrency, command.OccurredAt, cancellationToken);

        var outLeg = Transaction.CreateTransferLeg(
            fromAccount.Id,
            transferId,
            TransactionDirection.Outflow,
            command.OccurredAt,
            new Money(command.AmountOut, fromAccount.Currency),
            command.Description,
            tenant.TenantId,
            outSnapshot);

        var inLeg = Transaction.CreateTransferLeg(
            toAccount.Id,
            transferId,
            TransactionDirection.Inflow,
            command.OccurredAt,
            new Money(amountIn, toAccount.Currency),
            command.Description,
            tenant.TenantId,
            inSnapshot);

        await repository.AddAsync(outLeg, cancellationToken);
        await repository.AddAsync(inLeg, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        await PublishCreatedAsync(outLeg, tenant, events, cancellationToken);
        await PublishCreatedAsync(inLeg, tenant, events, cancellationToken);

        return new TransferResponse(
            transferId,
            TransactionHandlers.ToResponse(outLeg, inLeg.AccountId),
            TransactionHandlers.ToResponse(inLeg, outLeg.AccountId));
    }

    public static async Task<TransferResponse> Handle(
        UpdateTransferCommand command,
        ITransactionRepository repository,
        IAccountRepository accountRepository,
        ITenantContext tenant,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        var legs = await repository.GetByTransferIdAsync(command.TransferId, cancellationToken);
        if (legs.Count != 2)
        {
            throw new EntityNotFoundException("Transferência", command.TransferId);
        }

        var (fromAccount, toAccount, amountIn) = await ValidateAccountsAndAmountInAsync(
            command.FromAccountId,
            command.ToAccountId,
            command.AmountOut,
            command.AmountIn,
            accountRepository,
            cancellationToken);

        // R5 — a perna identifica-se pela direção, não pela ordem devolvida.
        var outLeg = legs.Single(l => l.Direction == TransactionDirection.Outflow);
        var inLeg = legs.Single(l => l.Direction == TransactionDirection.Inflow);

        outLeg.UpdateTransferLeg(
            fromAccount.Id,
            command.OccurredAt,
            new Money(command.AmountOut, fromAccount.Currency),
            command.Description);

        inLeg.UpdateTransferLeg(
            toAccount.Id,
            command.OccurredAt,
            new Money(amountIn, toAccount.Currency),
            command.Description);

        repository.Update(outLeg);
        repository.Update(inLeg);
        await repository.SaveChangesAsync(cancellationToken);

        await PublishUpdatedAsync(outLeg, tenant, events, cancellationToken);
        await PublishUpdatedAsync(inLeg, tenant, events, cancellationToken);

        return new TransferResponse(
            command.TransferId,
            TransactionHandlers.ToResponse(outLeg, inLeg.AccountId),
            TransactionHandlers.ToResponse(inLeg, outLeg.AccountId));
    }

    public static async Task<bool> Handle(
        DeleteTransferCommand command,
        ITransactionRepository repository,
        ITenantContext tenant,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        var legs = await repository.GetByTransferIdAsync(command.TransferId, cancellationToken);
        if (legs.Count != 2)
        {
            throw new EntityNotFoundException("Transferência", command.TransferId);
        }

        foreach (var leg in legs)
        {
            leg.ArchiveTransferLeg();
        }

        foreach (var leg in legs)
        {
            repository.Update(leg);
        }

        await repository.SaveChangesAsync(cancellationToken);

        // Phase 6.5 §0.6 — mesmo padrão de ArchiveTransactionCommand: o
        // progresso dos orçamentos exclui transações apagadas; o evento faz
        // o subscriber recalcular.
        foreach (var leg in legs)
        {
            await PublishUpdatedAsync(leg, tenant, events, cancellationToken);
        }

        return true;
    }

    public static async Task<TransferResponse> Handle(
        ConvertToTransferCommand command,
        ITransactionRepository repository,
        IAccountRepository accountRepository,
        ITenantContext tenant,
        ITenantCurrencyResolver currency,
        IExchangeRateService exchangeRates,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        var transaction = await repository.GetByIdAsync(command.TransactionId, cancellationToken)
            ?? throw new EntityNotFoundException("Transação", command.TransactionId);
        if (transaction.Kind != TransactionKind.Regular)
        {
            throw new TransactionNotRegularException();
        }

        var counterpartAccount = await accountRepository.GetByIdAsync(command.CounterpartAccountId, cancellationToken)
            ?? throw new EntityNotFoundException("Conta", command.CounterpartAccountId);
        if (counterpartAccount.Id == transaction.AccountId)
        {
            throw new TransferAccountsMustDifferException();
        }

        var transferId = GuidV7.NewId();

        if (command.CounterpartTransactionId is { } counterpartTransactionId)
        {
            return await ConvertLinkingExistingAsync(
                transaction, counterpartAccount, counterpartTransactionId, transferId,
                repository, tenant, events, cancellationToken);
        }

        return await ConvertCreatingCounterpartAsync(
            transaction, counterpartAccount, transferId,
            repository, tenant, currency, exchangeRates, events, cancellationToken);
    }

    private static async Task<TransferResponse> ConvertLinkingExistingAsync(
        Transaction transaction,
        Account counterpartAccount,
        Guid counterpartTransactionId,
        Guid transferId,
        ITransactionRepository repository,
        ITenantContext tenant,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        var counterpart = await repository.GetByIdAsync(counterpartTransactionId, cancellationToken)
            ?? throw new EntityNotFoundException("Transação", counterpartTransactionId);

        // Erro de chamada (não de domínio) — o frontend nunca deve deixar
        // escolher uma transação de outra conta que não a contraparte pedida.
        if (counterpart.AccountId != counterpartAccount.Id)
        {
            throw new ArgumentException(
                "A transação selecionada não pertence à conta indicada.", nameof(counterpartTransactionId));
        }

        if (counterpart.Kind != TransactionKind.Regular)
        {
            throw new TransactionNotRegularException();
        }

        if (counterpart.TransferId is not null)
        {
            throw new TransferCounterpartAlreadyLinkedException();
        }

        if (counterpart.Direction == transaction.Direction)
        {
            throw new TransferCounterpartSameDirectionException();
        }

        if (counterpartAccount.Currency == transaction.Amount.Currency
            && counterpart.Amount.Amount != transaction.Amount.Amount)
        {
            throw new TransferCounterpartAmountMismatchException();
        }

        transaction.ConvertToTransferLeg(transferId);
        counterpart.ConvertToTransferLeg(transferId);

        repository.Update(transaction);
        repository.Update(counterpart);
        await repository.SaveChangesAsync(cancellationToken);

        await PublishUpdatedAsync(transaction, tenant, events, cancellationToken);
        await PublishUpdatedAsync(counterpart, tenant, events, cancellationToken);

        var (outLeg, inLeg) = OrderLegs(transaction, counterpart);
        return new TransferResponse(
            transferId,
            TransactionHandlers.ToResponse(outLeg, inLeg.AccountId),
            TransactionHandlers.ToResponse(inLeg, outLeg.AccountId));
    }

    private static async Task<TransferResponse> ConvertCreatingCounterpartAsync(
        Transaction transaction,
        Account counterpartAccount,
        Guid transferId,
        ITransactionRepository repository,
        ITenantContext tenant,
        ITenantCurrencyResolver currency,
        IExchangeRateService exchangeRates,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        // R3 — sem transação de contraparte para amarrar o valor convertido,
        // só se pode criar automaticamente entre contas da mesma moeda.
        if (counterpartAccount.Currency != transaction.Amount.Currency)
        {
            throw new TransferCounterpartCurrencyMismatchException();
        }

        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        var snapshot = await exchangeRates.ResolveAsync(
            counterpartAccount.Currency, primaryCurrency, transaction.OccurredAt, cancellationToken);

        var oppositeDirection = transaction.Direction == TransactionDirection.Outflow
            ? TransactionDirection.Inflow
            : TransactionDirection.Outflow;

        var newLeg = Transaction.CreateTransferLeg(
            counterpartAccount.Id,
            transferId,
            oppositeDirection,
            transaction.OccurredAt,
            new Money(transaction.Amount.Amount, counterpartAccount.Currency),
            transaction.Description,
            tenant.TenantId,
            snapshot);

        transaction.ConvertToTransferLeg(transferId);

        await repository.AddAsync(newLeg, cancellationToken);
        repository.Update(transaction);
        await repository.SaveChangesAsync(cancellationToken);

        await PublishUpdatedAsync(transaction, tenant, events, cancellationToken);
        await PublishCreatedAsync(newLeg, tenant, events, cancellationToken);

        var (outLeg, inLeg) = OrderLegs(transaction, newLeg);
        return new TransferResponse(
            transferId,
            TransactionHandlers.ToResponse(outLeg, inLeg.AccountId),
            TransactionHandlers.ToResponse(inLeg, outLeg.AccountId));
    }

    /// <summary>
    /// Valida R1 (contas existem), a regra "contas diferentes" e R2 (moeda
    /// diferente exige AmountIn explícito) — partilhado entre Create e
    /// Update, que carregam os mesmos campos de conta/valor.
    /// </summary>
    private static async Task<(Account FromAccount, Account ToAccount, decimal AmountIn)> ValidateAccountsAndAmountInAsync(
        Guid fromAccountId,
        Guid toAccountId,
        decimal amountOut,
        decimal? amountIn,
        IAccountRepository accountRepository,
        CancellationToken cancellationToken)
    {
        if (fromAccountId == toAccountId)
        {
            throw new TransferAccountsMustDifferException();
        }

        var fromAccount = await accountRepository.GetByIdAsync(fromAccountId, cancellationToken)
            ?? throw new EntityNotFoundException("Conta", fromAccountId);
        var toAccount = await accountRepository.GetByIdAsync(toAccountId, cancellationToken)
            ?? throw new EntityNotFoundException("Conta", toAccountId);

        var crossCurrency = fromAccount.Currency != toAccount.Currency;
        if (crossCurrency && amountIn is null)
        {
            throw new TransferAmountInRequiredException();
        }

        var resolvedAmountIn = crossCurrency ? amountIn!.Value : amountOut;
        return (fromAccount, toAccount, resolvedAmountIn);
    }

    private static (Transaction OutLeg, Transaction InLeg) OrderLegs(Transaction a, Transaction b)
        => a.Direction == TransactionDirection.Outflow ? (a, b) : (b, a);

    private static Task PublishCreatedAsync(
        Transaction leg, ITenantContext tenant, IIntegrationEventPublisher events, CancellationToken cancellationToken)
        => events.PublishAsync(
            new TransactionCreatedIntegrationEvent(
                leg.Id,
                tenant.TenantId.Value,
                leg.AccountId,
                null,
                leg.Amount.Amount,
                leg.Amount.Currency,
                leg.OccurredAt,
                DateTimeOffset.UtcNow),
            cancellationToken);

    private static Task PublishUpdatedAsync(
        Transaction leg, ITenantContext tenant, IIntegrationEventPublisher events, CancellationToken cancellationToken)
        => events.PublishAsync(
            new TransactionUpdatedIntegrationEvent(
                leg.Id,
                tenant.TenantId.Value,
                leg.AccountId,
                leg.CategoryId,
                leg.Amount.Amount,
                leg.Amount.Currency,
                leg.OccurredAt,
                DateTimeOffset.UtcNow),
            cancellationToken);
}
