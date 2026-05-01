using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.RecurringRules;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class RecurringRuleRepository : IRecurringRuleRepository
{
    private readonly FinancialDbContext _db;

    public RecurringRuleRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public Task<RecurringRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.RecurringRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<List<RecurringRule>> ListAsync(CancellationToken cancellationToken)
        => await _db.RecurringRules
            .OrderBy(r => r.NextOccurrence != null ? 0 : 1)
            .ThenBy(r => r.NextOccurrence ?? DateOnly.MaxValue)
            .ThenBy(r => r.Description)
            .ToListAsync(cancellationToken);

    public Task AddAsync(RecurringRule rule, CancellationToken cancellationToken)
        => _db.RecurringRules.AddAsync(rule, cancellationToken).AsTask();

    public void Update(RecurringRule rule) => _db.RecurringRules.Update(rule);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);

    public Task<List<RecurringRule>> GetActiveRulesDueAsync(DateOnly runDate, CancellationToken cancellationToken)
        => _db.RecurringRules
            .Where(r => r.NextOccurrence != null
                        && r.NextOccurrence <= runDate
                        && r.IsActive)
            .ToListAsync(cancellationToken);

    public async Task<bool> HasMaterializedAsync(Guid ruleId, DateOnly occurrenceDate, CancellationToken cancellationToken)
        => await _db.Transactions.AnyAsync(
            t => t.RecurringRuleId == ruleId
                 && t.OccurredAt.Date == occurrenceDate.ToDateTime(TimeOnly.MinValue)
                 && t.DeletedAt == null,
            cancellationToken);
}
