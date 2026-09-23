using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.InstallmentPlans;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class InstallmentPlanRepository : IInstallmentPlanRepository
{
    private readonly FinancialDbContext _db;

    public InstallmentPlanRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public Task<InstallmentPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.InstallmentPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<InstallmentPlan>> ListAsync(Guid? accountId, CancellationToken cancellationToken)
    {
        var query = _db.InstallmentPlans.AsQueryable();
        if (accountId is { } id)
        {
            query = query.Where(p => p.AccountId == id);
        }

        return await query
            .OrderByDescending(p => p.FirstInstallmentDate)
            .ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsForPurchaseAsync(Guid purchaseTransactionId, Guid? exceptPlanId, CancellationToken cancellationToken)
        => _db.InstallmentPlans.AnyAsync(
            p => p.PurchaseTransactionId == purchaseTransactionId && (exceptPlanId == null || p.Id != exceptPlanId),
            cancellationToken);

    public Task AddAsync(InstallmentPlan plan, CancellationToken cancellationToken)
        => _db.InstallmentPlans.AddAsync(plan, cancellationToken).AsTask();

    public void Update(InstallmentPlan plan) => _db.InstallmentPlans.Update(plan);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);
}
