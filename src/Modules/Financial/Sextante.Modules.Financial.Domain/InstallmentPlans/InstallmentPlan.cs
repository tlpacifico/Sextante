using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.InstallmentPlans;

/// <summary>
/// Plano de prestações de uma compra num cartão de crédito (Phase 6.5
/// grupo 6). Informativo: a compra entra nos relatórios pelo total na data
/// da compra (requirements.md D8); o plano dá o calendário e as prestações
/// ainda por faturar, que o próximo pagamento do cartão desconta.
/// <see cref="FirstInstallmentDate"/> é a data da prestação n.º 1, mesmo em
/// planos que começaram antes de usar a app; <see cref="InstallmentsAlreadyPaid"/>
/// conta as pagas antes disso (ou antecipadamente). <see cref="PurchaseDate"/>
/// diz se a compra já estava na dívida de um fecho — só então as prestações
/// por faturar se descontam no próximo pagamento.
/// </summary>
public sealed class InstallmentPlan : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int DescriptionMaxLength = 200;
    public const int MinInstallments = 2;
    public const int MaxInstallments = 120;
    public const decimal MaxAnnualRate = 100m;

    private InstallmentPlan()
    {
        Description = string.Empty;
        TotalAmount = null!;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }

    /// <summary>Cartão do plano; imutável (soft reference, sem FK).</summary>
    public Guid AccountId { get; private set; }

    /// <summary>Compra que originou o plano, se houver (soft reference, sem FK).</summary>
    public Guid? PurchaseTransactionId { get; private set; }

    /// <summary>
    /// Data da compra. Num plano ligado vem da transação (a Application
    /// preenche-a); num plano manual é indicada pelo utilizador.
    /// </summary>
    public DateOnly PurchaseDate { get; private set; }

    public string Description { get; private set; }
    public Money TotalAmount { get; private set; }
    public int InstallmentCount { get; private set; }
    public int InstallmentsAlreadyPaid { get; private set; }
    public DateOnly FirstInstallmentDate { get; private set; }

    /// <summary>TAN em % (0–100), só informativa — os juros vêm do extrato.</summary>
    public decimal? AnnualRate { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static InstallmentPlan Create(
        TenantId tenantId,
        Guid accountId,
        Guid? purchaseTransactionId,
        DateOnly purchaseDate,
        string description,
        Money totalAmount,
        int installmentCount,
        int installmentsAlreadyPaid,
        DateOnly firstInstallmentDate,
        decimal? annualRate)
    {
        var normalized = Validate(description, totalAmount, installmentCount, installmentsAlreadyPaid, annualRate);

        return new InstallmentPlan
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            AccountId = accountId,
            PurchaseTransactionId = purchaseTransactionId,
            PurchaseDate = purchaseDate,
            Description = normalized,
            TotalAmount = totalAmount,
            InstallmentCount = installmentCount,
            InstallmentsAlreadyPaid = installmentsAlreadyPaid,
            FirstInstallmentDate = firstInstallmentDate,
            AnnualRate = annualRate,
        };
    }

    public void Update(
        Guid? purchaseTransactionId,
        DateOnly purchaseDate,
        string description,
        Money totalAmount,
        int installmentCount,
        int installmentsAlreadyPaid,
        DateOnly firstInstallmentDate,
        decimal? annualRate)
    {
        if (!string.Equals(totalAmount.Currency, TotalAmount.Currency, StringComparison.Ordinal))
        {
            throw new InstallmentPlanCurrencyMismatchException();
        }

        Description = Validate(description, totalAmount, installmentCount, installmentsAlreadyPaid, annualRate);
        PurchaseTransactionId = purchaseTransactionId;
        PurchaseDate = purchaseDate;
        TotalAmount = totalAmount;
        InstallmentCount = installmentCount;
        InstallmentsAlreadyPaid = installmentsAlreadyPaid;
        FirstInstallmentDate = firstInstallmentDate;
        AnnualRate = annualRate;
    }

    public void Archive()
    {
        DeletedAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<Installment> Schedule()
        => InstallmentSchedule.Build(TotalAmount.Amount, InstallmentCount, FirstInstallmentDate);

    /// <summary>Paga antes da app (ou antecipadamente) ou com data já passada.</summary>
    public bool IsPaid(Installment installment, DateOnly today)
        => installment.Number <= InstallmentsAlreadyPaid || installment.Date <= today;

    /// <summary>
    /// Soma das prestações ainda por faturar a <paramref name="date"/>: não
    /// contadas como já pagas e com data posterior.
    /// </summary>
    public decimal UnbilledAfter(DateOnly date)
        => Schedule()
            .Where(i => i.Number > InstallmentsAlreadyPaid && i.Date > date)
            .Sum(i => i.Amount);

    /// <summary>"Prestação X de N": o maior entre as já pagas e as com data passada.</summary>
    public int InstallmentsPaidOrDue(DateOnly today)
    {
        var dated = Schedule().Count(i => i.Date <= today);
        return Math.Min(InstallmentCount, Math.Max(InstallmentsAlreadyPaid, dated));
    }

    public Installment? NextInstallment(DateOnly today)
        => Schedule().FirstOrDefault(i => i.Number > InstallmentsAlreadyPaid && i.Date > today);

    private static string Validate(
        string description,
        Money totalAmount,
        int installmentCount,
        int installmentsAlreadyPaid,
        decimal? annualRate)
    {
        var normalized = description?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > DescriptionMaxLength)
        {
            throw new InstallmentPlanDescriptionRequiredException();
        }

        if (totalAmount.Amount <= 0m)
        {
            throw new InstallmentPlanTotalMustBePositiveException();
        }

        if (installmentCount < MinInstallments || installmentCount > MaxInstallments)
        {
            throw new InstallmentPlanCountOutOfRangeException();
        }

        if (installmentsAlreadyPaid < 0 || installmentsAlreadyPaid >= installmentCount)
        {
            throw new InstallmentPlanAlreadyPaidOutOfRangeException();
        }

        if (annualRate is < 0m or > MaxAnnualRate)
        {
            throw new InstallmentPlanAnnualRateOutOfRangeException();
        }

        return normalized;
    }
}
