namespace Sextante.Modules.Financial.Domain.Budgets;

public interface IBudgetAlertRepository
{
    Task<BudgetAlert?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Verifica se já existe um alerta não-soft-deleted para o
    /// (budgetId, threshold). Usado pelo dispatch handler para
    /// idempotência (defesa em camadas — unique index também protege).
    /// </summary>
    Task<bool> ExistsAsync(Guid budgetId, int threshold, CancellationToken cancellationToken);

    /// <summary>
    /// Lista todos os alertas não-acknowledged do tenant atual,
    /// ordenados por <c>TriggeredAt DESC</c>. Usado pelo banner.
    /// </summary>
    Task<IReadOnlyList<BudgetAlert>> ListActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Lista alertas (acknowledged ou não) associados a um budget.
    /// Usado pelo <c>DeleteBudgetHandler</c> para soft-deletar
    /// alerts em cascata.
    /// </summary>
    Task<IReadOnlyList<BudgetAlert>> ListForBudgetAsync(Guid budgetId, CancellationToken cancellationToken);

    Task AddAsync(BudgetAlert alert, CancellationToken cancellationToken);
    void Update(BudgetAlert alert);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
