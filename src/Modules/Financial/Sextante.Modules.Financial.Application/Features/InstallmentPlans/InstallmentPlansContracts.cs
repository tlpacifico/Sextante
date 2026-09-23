using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.InstallmentPlans;

/// <summary>
/// Plano de prestações (Phase 6.5 grupo 6). O total vem na moeda do cartão.
/// <see cref="FirstInstallmentDate"/> é a data da prestação n.º 1.
/// <see cref="PurchaseDate"/> só conta em planos manuais — num plano ligado
/// vem sempre da transação de compra.
/// </summary>
public sealed record CreateInstallmentPlanCommand(
    Guid AccountId,
    Guid? PurchaseTransactionId,
    DateOnly PurchaseDate,
    string Description,
    decimal TotalAmount,
    int InstallmentCount,
    int InstallmentsAlreadyPaid,
    DateOnly FirstInstallmentDate,
    decimal? AnnualRate);

/// <summary>O cartão do plano não muda (apagar e criar de novo).</summary>
public sealed record UpdateInstallmentPlanCommand(
    Guid Id,
    Guid? PurchaseTransactionId,
    DateOnly PurchaseDate,
    string Description,
    decimal TotalAmount,
    int InstallmentCount,
    int InstallmentsAlreadyPaid,
    DateOnly FirstInstallmentDate,
    decimal? AnnualRate);

public sealed record ArchiveInstallmentPlanCommand(Guid Id);

public sealed record GetInstallmentPlanQuery(Guid Id);

public sealed record ListInstallmentPlansQuery(Guid? AccountId);

public sealed record InstallmentResponse(int Number, DateOnly Date, Money Amount, bool Paid);

/// <summary>
/// Plano com o calendário completo e os agregados a hoje:
/// <see cref="InstallmentsPaidOrDue"/> é o "X" de "prestação X de N";
/// <see cref="RemainingAmount"/> soma as prestações ainda por faturar.
/// </summary>
public sealed record InstallmentPlanResponse(
    Guid Id,
    Guid AccountId,
    Guid? PurchaseTransactionId,
    DateOnly PurchaseDate,
    string Description,
    Money TotalAmount,
    int InstallmentCount,
    int InstallmentsAlreadyPaid,
    DateOnly FirstInstallmentDate,
    decimal? AnnualRate,
    Money InstallmentAmount,
    int InstallmentsPaidOrDue,
    Money RemainingAmount,
    DateOnly? NextInstallmentDate,
    bool IsActive,
    IReadOnlyList<InstallmentResponse> Schedule,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
