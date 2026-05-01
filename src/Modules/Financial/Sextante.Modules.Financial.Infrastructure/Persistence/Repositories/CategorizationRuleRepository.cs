using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.CategorizationRules;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class CategorizationRuleRepository : ICategorizationRuleRepository
{
    private readonly FinancialDbContext _db;

    public CategorizationRuleRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public Task<CategorizationRule?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.CategorizationRules.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<CategorizationRule>> ListAsync(CancellationToken ct)
        => await _db.CategorizationRules
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CategorizationRule>> ListActiveAsync(CancellationToken ct)
        => await _db.CategorizationRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

    public Task AddAsync(CategorizationRule rule, CancellationToken ct)
        => _db.CategorizationRules.AddAsync(rule, ct).AsTask();

    public void Update(CategorizationRule rule) => _db.CategorizationRules.Update(rule);

    public Task<int> SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);

    public Task<int> CountByPriorityAsync(int priority, Guid? excludeId, CancellationToken ct)
    {
        var query = _db.CategorizationRules.Where(r => r.Priority == priority);
        if (excludeId is not null)
            query = query.Where(r => r.Id != excludeId.Value);
        return query.CountAsync(ct);
    }
}
