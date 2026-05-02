using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Budgets;

/// <summary>
/// Meta orçamental para uma <c>Category</c> num <c>BudgetPeriod</c>
/// (mês civil). Phase 5b §requirements: 1 Budget por
/// (tenant, category, year, month) — enforced via unique partial
/// index na DB. Limit em moeda específica; conversão para somar
/// transactions é feita on-demand pela query (Phase 3 ECB rates).
/// </summary>
public sealed class Budget : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int DefaultThreshold = 80;
    public const int MinThreshold = 1;
    public const int MaxThreshold = 99;
    public const int NotesMaxLength = 500;

    private Budget()
    {
        Limit = null!;
        Period = null!;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public Guid CategoryId { get; private set; }
    public BudgetPeriod Period { get; private set; }
    public Money Limit { get; private set; }
    public int AlertThresholdPercent { get; private set; }
    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static Budget Create(
        TenantId tenantId,
        Guid categoryId,
        BudgetPeriod period,
        Money limit,
        int? alertThresholdPercent,
        string? notes)
    {
        var threshold = alertThresholdPercent ?? DefaultThreshold;

        Validate(limit, threshold, notes);

        return new Budget
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            CategoryId = categoryId,
            Period = period,
            Limit = limit,
            AlertThresholdPercent = threshold,
            Notes = NormalizeNotes(notes),
        };
    }

    public void UpdateLimit(Money newLimit)
    {
        if (newLimit.Amount <= 0m)
        {
            throw new BudgetLimitMustBePositiveException();
        }

        Limit = newLimit;
    }

    public void UpdateThreshold(int threshold)
    {
        if (threshold < MinThreshold || threshold > MaxThreshold)
        {
            throw new BudgetThresholdInvalidException(threshold);
        }

        AlertThresholdPercent = threshold;
    }

    public void UpdateNotes(string? notes)
    {
        if (notes is not null && notes.Trim().Length > NotesMaxLength)
        {
            throw new BudgetNotesTooLongException(NotesMaxLength);
        }

        Notes = NormalizeNotes(notes);
    }

    public void Archive()
    {
        DeletedAt = DateTimeOffset.UtcNow;
    }

    private static void Validate(Money limit, int threshold, string? notes)
    {
        if (limit.Amount <= 0m)
        {
            throw new BudgetLimitMustBePositiveException();
        }

        if (threshold < MinThreshold || threshold > MaxThreshold)
        {
            throw new BudgetThresholdInvalidException(threshold);
        }

        if (notes is not null && notes.Trim().Length > NotesMaxLength)
        {
            throw new BudgetNotesTooLongException(NotesMaxLength);
        }
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        return notes.Trim();
    }
}

public sealed class BudgetLimitMustBePositiveException : FinancialDomainException
{
    public BudgetLimitMustBePositiveException()
        : base("O limite do orçamento tem de ser maior que zero.") { }
}

public sealed class BudgetThresholdInvalidException : FinancialDomainException
{
    public BudgetThresholdInvalidException(int threshold)
        : base($"Threshold de alerta '{threshold}' inválido — esperado entre {Budget.MinThreshold} e {Budget.MaxThreshold}%.")
    {
        Threshold = threshold;
    }

    public int Threshold { get; }
}

public sealed class BudgetNotesTooLongException : FinancialDomainException
{
    public BudgetNotesTooLongException(int max)
        : base($"As notas do orçamento têm no máximo {max} caracteres.") { }
}

public sealed class BudgetCategoryMustBeExpenseException : FinancialDomainException
{
    public BudgetCategoryMustBeExpenseException()
        : base("Os orçamentos só suportam categorias de despesa.") { }
}

public sealed class BudgetDuplicateForCategoryException : FinancialDomainException
{
    public BudgetDuplicateForCategoryException(Guid categoryId, BudgetPeriod period)
        : base($"Já existe um orçamento para esta categoria no período {period}.")
    {
        CategoryId = categoryId;
        Period = period;
    }

    public Guid CategoryId { get; }
    public BudgetPeriod Period { get; }
}
