namespace Sextante.Modules.Financial.Domain.RecurringRules;

public interface IRecurringRuleRepository
{
    Task<RecurringRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<List<RecurringRule>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(RecurringRule rule, CancellationToken cancellationToken);
    void Update(RecurringRule rule);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<List<RecurringRule>> GetActiveRulesDueAsync(DateOnly runDate, CancellationToken cancellationToken);
    Task<bool> HasMaterializedAsync(Guid ruleId, DateOnly occurrenceDate, CancellationToken cancellationToken);
}
