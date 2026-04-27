using Sextante.Modules.Financial.Domain.Accounts;
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
        CancellationToken cancellationToken)
    {
        var primaryCurrency = await currency.GetPrimaryCurrencyAsync(cancellationToken);
        var account = Account.Create(
            command.Name,
            command.Type,
            new Money(command.OpeningBalanceAmount, primaryCurrency),
            tenant.TenantId);

        await repository.AddAsync(account, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return ToResponse(account);
    }

    public static async Task<AccountResponse?> Handle(
        UpdateAccountCommand command,
        IAccountRepository repository,
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

        return ToResponse(account);
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

        account.Archive();
        repository.Update(account);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static async Task<AccountResponse?> Handle(
        GetAccountByIdQuery query,
        IAccountRepository repository,
        CancellationToken cancellationToken)
    {
        var account = await repository.GetByIdAsync(query.Id, cancellationToken);
        return account is null ? null : ToResponse(account);
    }

    public static async Task<IReadOnlyList<AccountResponse>> Handle(
        ListAccountsQuery query,
        IAccountRepository repository,
        CancellationToken cancellationToken)
    {
        var accounts = await repository.ListAsync(cancellationToken);
        return accounts.Select(ToResponse).ToList();
    }

    private static AccountResponse ToResponse(Account account)
        => new(
            account.Id,
            account.Name,
            account.Type,
            account.OpeningBalance,
            account.CreatedAt,
            account.UpdatedAt);
}
