using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Application.Features.Transactions;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.InstallmentPlans;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.PublicApi.Events;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.Accounts;

[NonTransactional]
public static class AccountHandlers
{
    public static async Task<AccountResponse> Handle(
        CreateAccountCommand command,
        IAccountRepository repository,
        ITenantContext tenant,
        ITenantCurrencyResolver currency,
        ICurrencyDirectory currencyDirectory,
        CancellationToken cancellationToken)
    {
        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        var requestedCurrency = string.IsNullOrWhiteSpace(command.Currency)
            ? primaryCurrency
            : command.Currency.Trim().ToUpperInvariant();

        if (!await currencyDirectory.IsActiveAsync(requestedCurrency, cancellationToken))
        {
            throw new CurrencyNotActiveException(requestedCurrency);
        }

        var account = Account.Create(
            command.Name,
            command.Type,
            requestedCurrency,
            new Money(command.OpeningBalanceAmount, requestedCurrency),
            tenant.TenantId,
            command.OpeningBalanceDate);

        if (command.CreditCard is { } creditCard)
        {
            await ConfigureCreditCardAsync(account, creditCard, repository, cancellationToken);
        }

        await repository.AddAsync(account, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        // Conta acabada de criar não tem transações — o saldo atual é o
        // próprio saldo inicial, sem precisar de consultar a query de saldo.
        return ToResponse(account, account.OpeningBalance);
    }

    public static async Task<AccountResponse?> Handle(
        UpdateAccountCommand command,
        IAccountRepository repository,
        IAccountBalanceQuery balances,
        CancellationToken cancellationToken)
    {
        var account = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (account is null)
        {
            return null;
        }

        account.Rename(command.Name);
        account.ChangeType(command.Type);

        // PUT substitui as definições do cartão; ChangeType já as limpou se a
        // conta deixou de ser cartão.
        if (command.CreditCard is { } creditCard)
        {
            await ConfigureCreditCardAsync(account, creditCard, repository, cancellationToken);
        }
        else if (account.Type == AccountType.CreditCard)
        {
            account.RemoveCreditCardSettings();
        }

        repository.Update(account);
        await repository.SaveChangesAsync(cancellationToken);

        var currentBalance = await balances.GetBalanceAsync(account.Id, null, cancellationToken)
            ?? account.OpeningBalance;
        return ToResponse(account, currentBalance);
    }

    public static async Task<bool> Handle(
        ArchiveAccountCommand command,
        IAccountRepository repository,
        CancellationToken cancellationToken)
    {
        var account = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (account is null)
        {
            return false;
        }

        var activeCount = await repository.CountActiveTransactionsAsync(account.Id, cancellationToken);
        account.EnsureCanArchive(activeCount);

        account.Archive();
        repository.Update(account);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static async Task<AccountResponse?> Handle(
        GetAccountByIdQuery query,
        IAccountRepository repository,
        IAccountBalanceQuery balances,
        CancellationToken cancellationToken)
    {
        var account = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var currentBalance = await balances.GetBalanceAsync(account.Id, null, cancellationToken)
            ?? account.OpeningBalance;
        return ToResponse(account, currentBalance);
    }

    public static async Task<IReadOnlyList<AccountResponse>> Handle(
        ListAccountsQuery query,
        IAccountRepository repository,
        IAccountBalanceQuery balances,
        CancellationToken cancellationToken)
    {
        var accounts = await repository.ListAsync(cancellationToken);
        var currentBalances = await balances.GetCurrentBalancesAsync(cancellationToken);

        return accounts
            .Select(a => ToResponse(
                a,
                currentBalances.TryGetValue(a.Id, out var balance) ? balance : a.OpeningBalance))
            .ToList();
    }

    public static async Task<AccountBalanceResponse?> Handle(
        GetAccountBalanceQuery query,
        IAccountRepository accounts,
        IAccountBalanceQuery balances,
        CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        // Entre as duas leituras a conta pode ter sido arquivada em
        // concorrência — trata como "não encontrado" (404), tal como no
        // resto do módulo, em vez de forçar um saldo nulo com "!".
        var balance = await balances.GetBalanceAsync(query.AccountId, query.At, cancellationToken);
        return balance is null ? null : new AccountBalanceResponse(query.AccountId, query.At, balance);
    }

    /// <summary>
    /// Phase 6.5 grupo 4 — acerto de saldo. Compara o saldo calculado à data
    /// com o real indicado; se diferem, cria um único <c>Adjustment</c> pela
    /// diferença (entrada se o real é maior, saída se menor), pelo que o
    /// saldo à data passa a coincidir com o real.
    /// </summary>
    public static async Task<ReconcileAccountResponse?> Handle(
        ReconcileAccountCommand command,
        IAccountRepository accounts,
        IAccountBalanceQuery balances,
        ITransactionRepository transactions,
        ITenantContext tenant,
        ITenantCurrencyResolver currency,
        IExchangeRateService exchangeRates,
        IIntegrationEventPublisher events,
        CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(command.AccountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        // A data vem do calendário local do utilizador: em fusos à frente de
        // UTC (Lisboa no verão), entre as 00:00 e a 01:00 o "hoje" local ainda
        // é "amanhã" em UTC — tolera-se um dia; a hora do acerto é limitada
        // a "agora" mais abaixo, nunca fica no futuro.
        var now = DateTimeOffset.UtcNow;
        var latestAcceptedDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(1);
        if (command.Date > latestAcceptedDate)
        {
            throw new ReconciliationDateInFutureException();
        }

        // Antes da data do saldo inicial o acerto seria ignorado pelo cálculo
        // de saldo — não teria efeito.
        if (command.Date < account.OpeningBalanceDate)
        {
            throw new ReconciliationBeforeOpeningBalanceException();
        }

        var calculated = await balances.GetBalanceAsync(account.Id, command.Date, cancellationToken);
        if (calculated is null)
        {
            // Arquivada em concorrência — mesmo tratamento de GetAccountBalanceQuery.
            return null;
        }

        var actual = new Money(command.ActualBalance, account.Currency);
        var difference = command.ActualBalance - calculated.Amount;
        if (difference == 0m)
        {
            return new ReconcileAccountResponse(
                account.Id, command.Date, calculated, actual, new Money(0m, account.Currency), null);
        }

        // Meio-dia UTC do dia D: dentro do corte do saldo a D (< D+1 00:00Z)
        // e mostrado no próprio dia D em qualquer fuso até ±11h (às 23:59:59Z
        // aparecia no dia seguinte em UTC+1 — revisão final do grupo 4).
        // Nunca no futuro quando D é hoje.
        var noonOfDate = new DateTimeOffset(command.Date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var occurredAt = noonOfDate < now ? noonOfDate : now;

        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        var snapshot = await exchangeRates.ResolveAsync(
            account.Currency, primaryCurrency, occurredAt, cancellationToken);

        var adjustment = Transaction.CreateAdjustment(
            account.Id,
            difference > 0m ? TransactionDirection.Inflow : TransactionDirection.Outflow,
            occurredAt,
            new Money(Math.Abs(difference), account.Currency),
            "Acerto de saldo",
            tenant.TenantId,
            snapshot,
            now);

        await transactions.AddAsync(adjustment, cancellationToken);
        await transactions.SaveChangesAsync(cancellationToken);

        await events.PublishAsync(
            new TransactionCreatedIntegrationEvent(
                adjustment.Id,
                tenant.TenantId.Value,
                adjustment.AccountId,
                null,
                adjustment.Amount.Amount,
                adjustment.Amount.Currency,
                adjustment.OccurredAt,
                DateTimeOffset.UtcNow),
            cancellationToken);

        return new ReconcileAccountResponse(
            account.Id,
            command.Date,
            calculated,
            actual,
            new Money(difference, account.Currency),
            TransactionHandlers.ToResponse(adjustment));
    }

    /// <summary>
    /// Phase 6.5 grupo 5 — vista do cartão: dívida, disponível, ciclo corrente,
    /// extrato anterior e próximo pagamento, calculados a partir das
    /// definições e das transações (nada de extrato persistido).
    /// </summary>
    public static async Task<CreditCardViewResponse?> Handle(
        GetCreditCardViewQuery query,
        IAccountRepository accounts,
        IAccountBalanceQuery balances,
        ICreditCardActivityQuery activity,
        IInstallmentPlanRepository installmentPlans,
        CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        if (account.Type != AccountType.CreditCard)
        {
            throw new AccountNotCreditCardException();
        }

        var currentBalance = await balances.GetBalanceAsync(account.Id, null, cancellationToken);
        if (currentBalance is null)
        {
            // Arquivada em concorrência — mesmo tratamento de GetAccountBalanceQuery.
            return null;
        }

        var currency = account.Currency;
        Money InCurrency(decimal amount) => new(amount, currency);

        if (account.CreditCard is not { } settings)
        {
            return new CreditCardViewResponse(
                account.Id, currentBalance, InCurrency(Math.Max(0m, -currentBalance.Amount)),
                null, null, null, null, null, null, null, null);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentCycle = CreditCardCalendar.CycleContaining(today, settings.StatementClosingDay, settings.PaymentDueDay);
        var previousCycle = CreditCardCalendar.Previous(currentCycle, settings.StatementClosingDay, settings.PaymentDueDay);

        var balanceAtPreviousClose = await balances.GetBalanceAsync(account.Id, previousCycle.End, cancellationToken);
        if (balanceAtPreviousClose is null)
        {
            return null;
        }

        var movements = await activity.GetMovementsAsync(
            account.Id, previousCycle.Start, currentCycle.End, cancellationToken);

        // Grupo 6 — prestações ainda por faturar no último fecho.
        var plans = await installmentPlans.ListAsync(account.Id, cancellationToken);
        var unbilledAtPreviousClose = plans.Sum(p => p.UnbilledAfter(previousCycle.End));

        var statement = CreditCardStatementCalculator.Calculate(
            settings, today, currentBalance.Amount, balanceAtPreviousClose.Amount, movements, unbilledAtPreviousClose);

        // Só mostra quem paga se ainda for uma conta válida para isso (não
        // arquivada e não cartão) — revisão final do grupo 5.
        Guid? paymentAccountId = null;
        if (settings.PaymentAccountId is { } id
            && await accounts.GetByIdAsync(id, cancellationToken) is { Type: not AccountType.CreditCard })
        {
            paymentAccountId = id;
        }

        CreditCardCycleResponse ToCycleResponse(CreditCardCycleTotals totals) => new(
            totals.Cycle.Start,
            totals.Cycle.End,
            totals.Cycle.PaymentDueDate,
            InCurrency(totals.Spent),
            InCurrency(totals.PaymentsReceived));

        return new CreditCardViewResponse(
            account.Id,
            currentBalance,
            InCurrency(statement.CurrentDebt),
            ToSettingsResponse(settings),
            InCurrency(statement.Available),
            ToCycleResponse(statement.CurrentCycle),
            ToCycleResponse(statement.PreviousCycle),
            InCurrency(statement.PreviousClosingDebt),
            statement.NextPaymentDueDate,
            InCurrency(statement.NextPaymentAmount),
            paymentAccountId,
            InCurrency(statement.UnbilledInstallmentsAtPreviousClose));
    }

    /// <summary>
    /// A conta de pagamento é uma soft reference (sem FK): tem de existir no
    /// tenant (o filtro global do EF esconde as de outros tenants) e não ser
    /// um cartão. A própria conta é recusada pelo domínio.
    /// </summary>
    private static async Task ConfigureCreditCardAsync(
        Account account,
        CreditCardSettingsInput input,
        IAccountRepository repository,
        CancellationToken cancellationToken)
    {
        if (input.PaymentAccountId is { } paymentAccountId && paymentAccountId != account.Id)
        {
            var paymentAccount = await repository.GetByIdAsync(paymentAccountId, cancellationToken);
            if (paymentAccount is null || paymentAccount.Type == AccountType.CreditCard)
            {
                throw new CreditCardPaymentAccountInvalidException();
            }
        }

        account.ConfigureCreditCard(
            new Money(input.CreditLimit, account.Currency),
            input.StatementClosingDay,
            input.PaymentDueDay,
            input.PaymentAccountId);
    }

    private static CreditCardSettingsResponse ToSettingsResponse(CreditCardSettings settings)
        => new(settings.CreditLimit, settings.StatementClosingDay, settings.PaymentDueDay, settings.PaymentAccountId);

    private static AccountResponse ToResponse(Account account, Money currentBalance)
        => new(
            account.Id,
            account.Name,
            account.Type,
            account.Currency,
            account.OpeningBalance,
            account.CreatedAt,
            account.UpdatedAt,
            account.OpeningBalanceDate,
            currentBalance,
            account.CreditCard is { } settings ? ToSettingsResponse(settings) : null);
}
