namespace Sextante.Modules.Financial.Domain.Accounts;

public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(Account account, CancellationToken cancellationToken);
    void Update(Account account);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<int> CountActiveTransactionsAsync(Guid accountId, CancellationToken cancellationToken);
}
