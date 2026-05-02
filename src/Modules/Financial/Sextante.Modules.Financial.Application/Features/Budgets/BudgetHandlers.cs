using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.Budgets;

[NonTransactional]
public static class BudgetHandlers
{
    public static async Task<BudgetResponse> Handle(
        CreateBudgetCommand command,
        IBudgetRepository repository,
        ICategoryRepository categoryRepository,
        ITenantContext tenant,
        ICurrencyDirectory currencyDirectory,
        IBudgetProgressService progressService,
        CancellationToken cancellationToken)
    {
        var period = new BudgetPeriod(command.Year, command.Month);
        var currency = command.LimitCurrency.Trim().ToUpperInvariant();

        if (!await currencyDirectory.IsActiveAsync(currency, cancellationToken))
        {
            throw new CurrencyNotActiveException(currency);
        }

        var category = await categoryRepository.GetByIdAsync(command.CategoryId, cancellationToken)
            ?? throw new ArgumentException("Categoria não encontrada.", nameof(command));

        if (category.Kind != CategoryKind.Expense)
        {
            throw new BudgetCategoryMustBeExpenseException();
        }

        // Idempotência amigável: rejeita duplicate antes do unique
        // index disparar (UX melhor).
        var existing = await repository.GetForCategoryAsync(command.CategoryId, period, cancellationToken);
        if (existing is not null)
        {
            throw new BudgetDuplicateForCategoryException(command.CategoryId, period);
        }

        var limit = new Money(command.LimitAmount, currency);
        var budget = Budget.Create(
            tenant.TenantId,
            command.CategoryId,
            period,
            limit,
            command.AlertThresholdPercent,
            command.Notes);

        await repository.AddAsync(budget, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        var progress = await progressService.CalculateAsync(budget, asOfDate: null, cancellationToken);
        return ToResponse(budget, progress);
    }

    public static async Task<BudgetResponse?> Handle(
        UpdateBudgetCommand command,
        IBudgetRepository repository,
        ICurrencyDirectory currencyDirectory,
        IBudgetProgressService progressService,
        CancellationToken cancellationToken)
    {
        var budget = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (budget is null)
        {
            return null;
        }

        var currency = command.LimitCurrency.Trim().ToUpperInvariant();
        if (!await currencyDirectory.IsActiveAsync(currency, cancellationToken))
        {
            throw new CurrencyNotActiveException(currency);
        }

        budget.UpdateLimit(new Money(command.LimitAmount, currency));
        if (command.AlertThresholdPercent is { } threshold)
        {
            budget.UpdateThreshold(threshold);
        }
        budget.UpdateNotes(command.Notes);

        repository.Update(budget);
        await repository.SaveChangesAsync(cancellationToken);

        var progress = await progressService.CalculateAsync(budget, asOfDate: null, cancellationToken);
        return ToResponse(budget, progress);
    }

    public static async Task<bool> Handle(
        ArchiveBudgetCommand command,
        IBudgetRepository repository,
        IBudgetAlertRepository alertRepository,
        CancellationToken cancellationToken)
    {
        var budget = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (budget is null)
        {
            return false;
        }

        // Soft-delete cascade no handler (não no DB para preservar
        // audit). Phase 5b §Decisions.
        var alerts = await alertRepository.ListForBudgetAsync(budget.Id, cancellationToken);
        foreach (var alert in alerts)
        {
            alert.Archive();
            alertRepository.Update(alert);
        }

        budget.Archive();
        repository.Update(budget);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static async Task<BudgetResponse?> Handle(
        GetBudgetByIdQuery query,
        IBudgetRepository repository,
        IBudgetProgressService progressService,
        CancellationToken cancellationToken)
    {
        var budget = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (budget is null)
        {
            return null;
        }

        var progress = await progressService.CalculateAsync(budget, asOfDate: null, cancellationToken);
        return ToResponse(budget, progress);
    }

    public static async Task<IReadOnlyList<BudgetResponse>> Handle(
        ListBudgetsQuery query,
        IBudgetRepository repository,
        IBudgetProgressService progressService,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var year = query.Year ?? today.Year;
        var month = query.Month ?? today.Month;

        // Valida via VO ctor (range check); deixa propagar exception
        // PT-PT se inválido.
        _ = new BudgetPeriod(year, month);

        var budgets = await repository.ListAsync(year, month, cancellationToken);
        var responses = new List<BudgetResponse>(budgets.Count);

        foreach (var budget in budgets)
        {
            var progress = await progressService.CalculateAsync(budget, asOfDate: null, cancellationToken);
            responses.Add(ToResponse(budget, progress));
        }

        return responses;
    }

    public static async Task<BudgetProgressResponse?> Handle(
        GetBudgetProgressQuery query,
        IBudgetRepository repository,
        IBudgetProgressService progressService,
        CancellationToken cancellationToken)
    {
        var budget = await repository.GetByIdAsync(query.Id, cancellationToken);
        if (budget is null)
        {
            return null;
        }

        var progress = await progressService.CalculateAsync(budget, asOfDate: null, cancellationToken);
        return ToProgressResponse(progress);
    }

    public static async Task<IReadOnlyList<BudgetAlertResponse>> Handle(
        ListActiveBudgetAlertsQuery _,
        IBudgetAlertRepository alertRepository,
        IBudgetRepository budgetRepository,
        CancellationToken cancellationToken)
    {
        var alerts = await alertRepository.ListActiveAsync(cancellationToken);
        var responses = new List<BudgetAlertResponse>(alerts.Count);

        // Resolve CategoryId por alerta — banner mostra nome da
        // categoria. Faz N+1 mas N é pequeno (banner exibe top alerts).
        // Pode-se otimizar com join futuramente.
        foreach (var alert in alerts)
        {
            var budget = await budgetRepository.GetByIdAsync(alert.BudgetId, cancellationToken);
            if (budget is null)
            {
                continue;
            }
            responses.Add(new BudgetAlertResponse(
                alert.Id,
                alert.BudgetId,
                budget.CategoryId,
                alert.Threshold,
                alert.TriggeredAt,
                alert.SpentAtTrigger.Amount,
                alert.SpentAtTrigger.Currency,
                alert.Acknowledged,
                alert.AcknowledgedAt));
        }

        return responses;
    }

    public static async Task<bool> Handle(
        AcknowledgeBudgetAlertCommand command,
        IBudgetAlertRepository alertRepository,
        CancellationToken cancellationToken)
    {
        var alert = await alertRepository.GetByIdAsync(command.Id, cancellationToken);
        if (alert is null)
        {
            return false;
        }

        alert.Acknowledge();
        alertRepository.Update(alert);
        await alertRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static BudgetResponse ToResponse(Budget budget, BudgetProgress progress)
        => new(
            budget.Id,
            budget.CategoryId,
            budget.Period.Year,
            budget.Period.Month,
            budget.Limit.Amount,
            budget.Limit.Currency,
            budget.AlertThresholdPercent,
            budget.Notes,
            ToProgressResponse(progress),
            budget.CreatedAt,
            budget.UpdatedAt);

    private static BudgetProgressResponse ToProgressResponse(BudgetProgress progress)
        => new(
            progress.Limit.Amount,
            progress.Limit.Currency,
            progress.Spent.Amount,
            progress.Remaining.Amount,
            progress.PercentUsed,
            progress.ProjectedEndOfPeriod?.Amount,
            progress.HasIncompleteRates);
}
