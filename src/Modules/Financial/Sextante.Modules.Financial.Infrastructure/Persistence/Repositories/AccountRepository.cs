using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.Accounts;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class AccountRepository : IAccountRepository
{
    private readonly FinancialDbContext _db;

    public AccountRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken)
        => await _db.Accounts
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

    public Task AddAsync(Account account, CancellationToken cancellationToken)
        => _db.Accounts.AddAsync(account, cancellationToken).AsTask();

    public void Update(Account account) => _db.Accounts.Update(account);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);

    public Task<int> CountActiveTransactionsAsync(Guid accountId, CancellationToken cancellationToken)
        => _db.Transactions.CountAsync(t => t.AccountId == accountId, cancellationToken);
}
