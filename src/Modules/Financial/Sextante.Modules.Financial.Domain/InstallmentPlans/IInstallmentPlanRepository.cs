namespace Sextante.Modules.Financial.Domain.InstallmentPlans;

/// <summary>
/// Repositório de <see cref="InstallmentPlan"/>. Tenant e soft-delete via
/// Global Query Filter + RLS.
/// </summary>
public interface IInstallmentPlanRepository
{
    Task<InstallmentPlan?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Planos do tenant; <paramref name="accountId"/> filtra por cartão.</summary>
    Task<IReadOnlyList<InstallmentPlan>> ListAsync(Guid? accountId, CancellationToken cancellationToken);

    /// <summary>Há outro plano ativo para esta compra (excluindo <paramref name="exceptPlanId"/>)?</summary>
    Task<bool> ExistsForPurchaseAsync(Guid purchaseTransactionId, Guid? exceptPlanId, CancellationToken cancellationToken);

    Task AddAsync(InstallmentPlan plan, CancellationToken cancellationToken);
    void Update(InstallmentPlan plan);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
