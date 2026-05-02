using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Budgets;

/// <summary>
/// Alerta persistido associado a um <see cref="Budget"/> quando um
/// threshold é cruzado por uma transaction. Idempotência via unique
/// index <c>(tenant_id, budget_id, threshold) WHERE deleted_at IS NULL</c>.
/// Acknowledgement separado de DeletedAt (acknowledge ≠ apagar — audit).
/// </summary>
public sealed class BudgetAlert : ITenantOwned, IAuditable, IFinancialAggregate
{
    private BudgetAlert()
    {
        SpentAtTrigger = null!;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public Guid BudgetId { get; private set; }
    public int Threshold { get; private set; }
    public DateTimeOffset TriggeredAt { get; private set; }
    public Money SpentAtTrigger { get; private set; }
    public bool Acknowledged { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static BudgetAlert Create(
        TenantId tenantId,
        Guid budgetId,
        int threshold,
        Money spentAtTrigger)
    {
        if (threshold < 1 || threshold > 100)
        {
            throw new BudgetAlertThresholdInvalidException(threshold);
        }

        if (spentAtTrigger.Amount < 0m)
        {
            throw new BudgetAlertSpentNegativeException();
        }

        return new BudgetAlert
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            BudgetId = budgetId,
            Threshold = threshold,
            TriggeredAt = DateTimeOffset.UtcNow,
            SpentAtTrigger = spentAtTrigger,
            Acknowledged = false,
            AcknowledgedAt = null,
        };
    }

    /// <summary>
    /// Marca o alerta como reconhecido. Idempotente: segunda chamada
    /// é no-op (não muda <c>AcknowledgedAt</c>).
    /// </summary>
    public void Acknowledge()
    {
        if (Acknowledged)
        {
            return;
        }

        Acknowledged = true;
        AcknowledgedAt = DateTimeOffset.UtcNow;
    }

    public void Archive()
    {
        DeletedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class BudgetAlertThresholdInvalidException : FinancialDomainException
{
    public BudgetAlertThresholdInvalidException(int threshold)
        : base($"Threshold de alerta '{threshold}' inválido — esperado entre 1 e 100.")
    {
        Threshold = threshold;
    }

    public int Threshold { get; }
}

public sealed class BudgetAlertSpentNegativeException : FinancialDomainException
{
    public BudgetAlertSpentNegativeException()
        : base("O valor gasto no momento do alerta não pode ser negativo.") { }
}

/// <summary>
/// Lançada pelo repo quando a inserção de um <see cref="BudgetAlert"/>
/// viola o unique index <c>(tenant_id, budget_id, threshold) WHERE
/// deleted_at IS NULL</c>. Race condition entre dispatchers paralelos —
/// o handler trata como no-op (alerta já existe).
/// </summary>
public sealed class BudgetAlertDuplicateException : FinancialDomainException
{
    public BudgetAlertDuplicateException(Exception inner)
        : base("Alerta duplicado para este budget+threshold.") { }
}
