using Sextante.Modules.Financial.Domain.Budgets;

namespace Sextante.Modules.Financial.Application.Features.Budgets;

/// <summary>
/// Calcula <see cref="BudgetProgress"/> para um Budget. Implementação
/// vive em Infrastructure (precisa de SQL agregador via DbContext).
/// </summary>
public interface IBudgetProgressService
{
    Task<BudgetProgress> CalculateAsync(
        Budget budget,
        DateOnly? asOfDate,
        CancellationToken cancellationToken);
}
