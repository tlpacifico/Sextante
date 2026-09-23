using Sextante.Modules.Financial.Domain.Accounts;

namespace Sextante.Modules.Financial.Application.Tests.TestSupport;

/// <summary>Repositório de contas em memória para testes de handlers.</summary>
public sealed class StubAccountRepository : IAccountRepository
{
    private readonly Dictionary<Guid, Account> _accounts;

    public StubAccountRepository(params Account[] accounts)
        => _accounts = accounts.ToDictionary(a => a.Id);

    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(_accounts.TryGetValue(id, out var a) ? a : null);

    public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Account>>(_accounts.Values.OrderBy(a => a.Name).ToList());

    public Task AddAsync(Account account, CancellationToken cancellationToken)
    {
        _accounts[account.Id] = account;
        return Task.CompletedTask;
    }

    public void Update(Account account) => _accounts[account.Id] = account;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<int> CountActiveTransactionsAsync(Guid accountId, CancellationToken cancellationToken)
        => Task.FromResult(0);
}
