using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.InstallmentPlans;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.InstallmentPlans;

/// <summary>
/// CRUD de planos de prestações (Phase 6.5 grupo 6). O cartão e a compra
/// são soft references validadas aqui: o filtro global do EF esconde as de
/// outros tenants, que dão 404 como as inexistentes.
/// </summary>
[NonTransactional]
public static class InstallmentPlanHandlers
{
    public static async Task<InstallmentPlanResponse> Handle(
        CreateInstallmentPlanCommand command,
        IInstallmentPlanRepository plans,
        IAccountRepository accounts,
        ITransactionRepository transactions,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var account = await LoadCardAsync(command.AccountId, accounts, cancellationToken);
        await ValidatePurchaseAsync(command.PurchaseTransactionId, account.Id, null, plans, transactions, cancellationToken);

        var plan = InstallmentPlan.Create(
            tenant.TenantId,
            account.Id,
            command.PurchaseTransactionId,
            command.Description,
            new Money(command.TotalAmount, account.Currency),
            command.InstallmentCount,
            command.InstallmentsAlreadyPaid,
            command.FirstInstallmentDate,
            command.AnnualRate);

        await plans.AddAsync(plan, cancellationToken);
        await plans.SaveChangesAsync(cancellationToken);

        return ToResponse(plan, Today());
    }

    public static async Task<InstallmentPlanResponse?> Handle(
        UpdateInstallmentPlanCommand command,
        IInstallmentPlanRepository plans,
        IAccountRepository accounts,
        ITransactionRepository transactions,
        CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(command.Id, cancellationToken);
        if (plan is null)
        {
            return null;
        }

        await ValidatePurchaseAsync(command.PurchaseTransactionId, plan.AccountId, plan.Id, plans, transactions, cancellationToken);

        plan.Update(
            command.PurchaseTransactionId,
            command.Description,
            new Money(command.TotalAmount, plan.TotalAmount.Currency),
            command.InstallmentCount,
            command.InstallmentsAlreadyPaid,
            command.FirstInstallmentDate,
            command.AnnualRate);

        plans.Update(plan);
        await plans.SaveChangesAsync(cancellationToken);

        return ToResponse(plan, Today());
    }

    public static async Task<bool> Handle(
        ArchiveInstallmentPlanCommand command,
        IInstallmentPlanRepository plans,
        CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(command.Id, cancellationToken);
        if (plan is null)
        {
            return false;
        }

        plan.Archive();
        plans.Update(plan);
        await plans.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static async Task<InstallmentPlanResponse?> Handle(
        GetInstallmentPlanQuery query,
        IInstallmentPlanRepository plans,
        CancellationToken cancellationToken)
    {
        var plan = await plans.GetByIdAsync(query.Id, cancellationToken);
        return plan is null ? null : ToResponse(plan, Today());
    }

    public static async Task<IReadOnlyList<InstallmentPlanResponse>> Handle(
        ListInstallmentPlansQuery query,
        IInstallmentPlanRepository plans,
        CancellationToken cancellationToken)
    {
        var today = Today();
        var list = await plans.ListAsync(query.AccountId, cancellationToken);
        return list.Select(p => ToResponse(p, today)).ToList();
    }

    internal static InstallmentPlanResponse ToResponse(InstallmentPlan plan, DateOnly today)
    {
        var currency = plan.TotalAmount.Currency;
        var schedule = plan.Schedule();
        var remaining = plan.UnbilledAfter(today);

        return new InstallmentPlanResponse(
            plan.Id,
            plan.AccountId,
            plan.PurchaseTransactionId,
            plan.Description,
            plan.TotalAmount,
            plan.InstallmentCount,
            plan.InstallmentsAlreadyPaid,
            plan.FirstInstallmentDate,
            plan.AnnualRate,
            new Money(schedule[0].Amount, currency),
            plan.InstallmentsPaidOrDue(today),
            new Money(remaining, currency),
            plan.NextInstallment(today)?.Date,
            remaining > 0m,
            schedule
                .Select(i => new InstallmentResponse(i.Number, i.Date, new Money(i.Amount, currency), plan.IsPaid(i, today)))
                .ToList(),
            plan.CreatedAt,
            plan.UpdatedAt);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Account> LoadCardAsync(Guid accountId, IAccountRepository accounts, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(accountId, cancellationToken)
            ?? throw new KeyNotFoundException($"Conta com ID '{accountId}' não encontrada.");

        if (account.Type != AccountType.CreditCard)
        {
            throw new InstallmentPlanRequiresCreditCardException();
        }

        return account;
    }

    /// <summary>
    /// A compra (se indicada) tem de ser uma despesa regular do mesmo cartão,
    /// sem outro plano ativo.
    /// </summary>
    private static async Task ValidatePurchaseAsync(
        Guid? purchaseTransactionId,
        Guid accountId,
        Guid? exceptPlanId,
        IInstallmentPlanRepository plans,
        ITransactionRepository transactions,
        CancellationToken cancellationToken)
    {
        if (purchaseTransactionId is not { } id)
        {
            return;
        }

        var purchase = await transactions.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Transação com ID '{id}' não encontrada.");

        if (purchase.AccountId != accountId
            || purchase.Kind != TransactionKind.Regular
            || purchase.Direction != TransactionDirection.Outflow)
        {
            throw new InstallmentPlanPurchaseInvalidException();
        }

        if (await plans.ExistsForPurchaseAsync(id, exceptPlanId, cancellationToken))
        {
            throw new InstallmentPlanPurchaseAlreadyLinkedException();
        }
    }
}
