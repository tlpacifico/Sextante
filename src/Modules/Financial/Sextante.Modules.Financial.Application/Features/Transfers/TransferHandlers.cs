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
        ITenantCurrencyResolver currency,
        IExchangeRateService exchangeRates,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        var legs = await repository.GetByTransferIdAsync(command.TransferId, cancellationToken);
        if (legs.Count != 2)
        {
            throw new KeyNotFoundException($"Transferência com ID '{command.TransferId}' não encontrada.");
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

        // Revisão da phase (I2) — o import deixa cada perna na data do seu
        // extrato (a entrada no cartão pode ser dias depois da saída da conta).
        // A data do comando é a da perna de saída; a de entrada desloca-se o
        // mesmo, para não se perder a diferença entre as duas.
        var inOccurredAt = inLeg.OccurredAt + (command.OccurredAt - outLeg.OccurredAt);

        // Achado da revisão final do grupo 3: mudar a conta de uma perna pode
        // mudar a moeda — recalcular sempre o câmbio (como Create já faz),
        // nunca deixar ExchangeRateToPrimary congelado na moeda antiga.
        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        var outSnapshot = await exchangeRates.ResolveAsync(
            fromAccount.Currency, primaryCurrency, command.OccurredAt, cancellationToken);
        var inSnapshot = await exchangeRates.ResolveAsync(
            toAccount.Currency, primaryCurrency, inOccurredAt, cancellationToken);

        outLeg.UpdateTransferLeg(
            fromAccount.Id,
            command.OccurredAt,
            new Money(command.AmountOut, fromAccount.Currency),
            command.Description,
            outSnapshot);

        inLeg.UpdateTransferLeg(
            toAccount.Id,
            inOccurredAt,
            new Money(amountIn, toAccount.Currency),
            command.Description,
            inSnapshot);

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
            throw new KeyNotFoundException($"Transferência com ID '{command.TransferId}' não encontrada.");
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

    // Achado da revisão final do grupo 3: POST /transfers devolvia um
    // Location apontando para este GET, que ainda não existia; e o frontend
    // precisava de uma forma de carregar as duas pernas reais (não um
    // palpite a partir de uma só linha da tabela) para editar em segurança
    // uma transferência entre moedas diferentes.
    public static async Task<TransferResponse?> Handle(
        GetTransferByIdQuery query,
        ITransactionRepository repository,
        CancellationToken cancellationToken)
    {
        var legs = await repository.GetByTransferIdAsync(query.TransferId, cancellationToken);
        if (legs.Count != 2)
        {
            return null;
        }

        var outLeg = legs.Single(l => l.Direction == TransactionDirection.Outflow);
        var inLeg = legs.Single(l => l.Direction == TransactionDirection.Inflow);

        return new TransferResponse(
            query.TransferId,
            TransactionHandlers.ToResponse(outLeg, inLeg.AccountId),
            TransactionHandlers.ToResponse(inLeg, outLeg.AccountId));
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
            ?? throw new KeyNotFoundException($"Transação com ID '{command.TransactionId}' não encontrada.");
        if (transaction.Kind != TransactionKind.Regular)
        {
            throw new TransactionNotRegularException();
        }

        var counterpartAccount = await accountRepository.GetByIdAsync(command.CounterpartAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"Conta com ID '{command.CounterpartAccountId}' não encontrada.");
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
            ?? throw new KeyNotFoundException($"Transação com ID '{counterpartTransactionId}' não encontrada.");

        // Achado da revisão final do grupo 3: isto já era tratado como um
        // "erro de chamada", mas um ArgumentException não apanhado dá 500 —
        // um pedido malformado continua a merecer um 400 limpo.
        if (counterpart.AccountId != counterpartAccount.Id)
        {
            throw new TransferCounterpartWrongAccountException();
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
            ?? throw new KeyNotFoundException($"Conta com ID '{fromAccountId}' não encontrada.");
        var toAccount = await accountRepository.GetByIdAsync(toAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"Conta com ID '{toAccountId}' não encontrada.");

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
