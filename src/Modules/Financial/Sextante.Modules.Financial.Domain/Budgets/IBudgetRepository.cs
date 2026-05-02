namespace Sextante.Modules.Financial.Domain.Budgets;

/// <summary>
/// Repositório de <see cref="Budget"/>. Filtragem por tenant é
/// automática via Global Query Filter + RLS — métodos não precisam
/// de aceitar TenantId como argumento.
/// </summary>
public interface IBudgetRepository
{
    Task<Budget?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Lista budgets ativos para o período (year, month) do tenant.
    /// Soft-deleted excluídos.
    /// </summary>
    Task<IReadOnlyList<Budget>> ListAsync(int year, int month, CancellationToken cancellationToken);

    /// <summary>
    /// Devolve o budget para a categoria + período se existir, ou null.
    /// Usado pelo <c>BudgetAlertDispatchHandler</c> para localizar
    /// o budget afetado por uma transaction recém-criada.
    /// </summary>
    Task<Budget?> GetForCategoryAsync(Guid categoryId, BudgetPeriod period, CancellationToken cancellationToken);

    Task AddAsync(Budget budget, CancellationToken cancellationToken);
    void Update(Budget budget);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
