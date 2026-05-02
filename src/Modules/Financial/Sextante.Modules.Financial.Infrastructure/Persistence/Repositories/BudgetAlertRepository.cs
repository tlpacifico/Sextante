using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sextante.Modules.Financial.Domain.Budgets;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class BudgetAlertRepository : IBudgetAlertRepository
{
    private readonly FinancialDbContext _db;

    public BudgetAlertRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public Task<BudgetAlert?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.BudgetAlerts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid budgetId, int threshold, CancellationToken cancellationToken)
        => _db.BudgetAlerts.AnyAsync(
            a => a.BudgetId == budgetId && a.Threshold == threshold,
            cancellationToken);

    public async Task<IReadOnlyList<BudgetAlert>> ListActiveAsync(CancellationToken cancellationToken)
        => await _db.BudgetAlerts
            .Where(a => !a.Acknowledged)
            .OrderByDescending(a => a.TriggeredAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BudgetAlert>> ListForBudgetAsync(Guid budgetId, CancellationToken cancellationToken)
        => await _db.BudgetAlerts
            .Where(a => a.BudgetId == budgetId)
            .ToListAsync(cancellationToken);

    public Task AddAsync(BudgetAlert alert, CancellationToken cancellationToken)
        => _db.BudgetAlerts.AddAsync(alert, cancellationToken).AsTask();

    public void Update(BudgetAlert alert) => _db.BudgetAlerts.Update(alert);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race condition entre dispatch handlers paralelos —
            // outro inseriu o mesmo (tenant, budget, threshold) primeiro.
            // Re-throw como domain exception para que o handler trate
            // como no-op sem expor EF Core ao Application layer.
            throw new BudgetAlertDuplicateException(ex);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException pg && pg.SqlState == "23505";
}
