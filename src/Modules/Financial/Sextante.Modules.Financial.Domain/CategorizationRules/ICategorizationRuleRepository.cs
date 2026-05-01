namespace Sextante.Modules.Financial.Domain.CategorizationRules;

public interface ICategorizationRuleRepository
{
    Task<CategorizationRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CategorizationRule>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CategorizationRule>> ListActiveAsync(CancellationToken cancellationToken);
    Task AddAsync(CategorizationRule rule, CancellationToken cancellationToken);
    void Update(CategorizationRule rule);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<int> CountByPriorityAsync(int priority, Guid? excludeId, CancellationToken cancellationToken);
}
