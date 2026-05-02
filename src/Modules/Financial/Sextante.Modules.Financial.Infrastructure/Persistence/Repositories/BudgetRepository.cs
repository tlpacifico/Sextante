using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.Budgets;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class BudgetRepository : IBudgetRepository
{
    private readonly FinancialDbContext _db;

    public BudgetRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.Budgets.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Budget>> ListAsync(int year, int month, CancellationToken cancellationToken)
        => await _db.Budgets
            .Where(b => b.Period.Year == year && b.Period.Month == month)
            .OrderBy(b => b.CategoryId)
            .ToListAsync(cancellationToken);

    public Task<Budget?> GetForCategoryAsync(Guid categoryId, BudgetPeriod period, CancellationToken cancellationToken)
        => _db.Budgets.FirstOrDefaultAsync(
            b => b.CategoryId == categoryId
                 && b.Period.Year == period.Year
                 && b.Period.Month == period.Month,
            cancellationToken);

    public Task AddAsync(Budget budget, CancellationToken cancellationToken)
        => _db.Budgets.AddAsync(budget, cancellationToken).AsTask();

    public void Update(Budget budget) => _db.Budgets.Update(budget);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);
}
