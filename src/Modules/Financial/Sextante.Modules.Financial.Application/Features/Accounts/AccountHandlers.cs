using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
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

        var balance = await balances.GetBalanceAsync(query.AccountId, query.At, cancellationToken);
        return new AccountBalanceResponse(query.AccountId, query.At, balance!);
    }

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
            currentBalance);
}
