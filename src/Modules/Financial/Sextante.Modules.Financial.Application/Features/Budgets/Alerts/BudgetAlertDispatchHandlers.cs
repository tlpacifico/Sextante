using Microsoft.Extensions.Logging;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.Modules.Financial.PublicApi.Events;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.Budgets.Alerts;

/// <summary>
/// Wolverine subscriber para <see cref="TransactionCreatedIntegrationEvent"/>
/// e <see cref="TransactionUpdatedIntegrationEvent"/>. Recalcula
/// progresso do Budget afetado (categoria + mês da tx) e insere
/// <see cref="BudgetAlert"/> em 80% (configurável) e 100% se
/// thresholds cruzados. Idempotente via unique index
/// <c>(tenant, budget, threshold)</c> + check Exists.
///
/// Sufixo `Handlers` (plural) para Wolverine descobrir via convenção
/// (Program.cs §145 `WithNameSuffix("Handlers")`).
/// </summary>
public sealed class BudgetAlertDispatchHandlers
{
    private readonly IBudgetRepository _budgetRepo;
    private readonly IBudgetAlertRepository _alertRepo;
    private readonly IBudgetProgressService _progressService;
    private readonly ILogger<BudgetAlertDispatchHandlers> _logger;

    public BudgetAlertDispatchHandlers(
        IBudgetRepository budgetRepo,
        IBudgetAlertRepository alertRepo,
        IBudgetProgressService progressService,
        ILogger<BudgetAlertDispatchHandlers> logger)
    {
        _budgetRepo = budgetRepo;
        _alertRepo = alertRepo;
        _progressService = progressService;
        _logger = logger;
    }

    public Task Handle(TransactionCreatedIntegrationEvent @event, CancellationToken ct)
        => HandleCommonAsync(@event.TenantId, @event.CategoryId, @event.OccurredAtTransaction, ct);

    public Task Handle(TransactionUpdatedIntegrationEvent @event, CancellationToken ct)
        => HandleCommonAsync(@event.TenantId, @event.CategoryId, @event.OccurredAtTransaction, ct);

    private async Task HandleCommonAsync(
        Guid tenantId,
        Guid? categoryId,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        if (categoryId is null || categoryId == Guid.Empty)
        {
            return;
        }

        var occurredDate = DateOnly.FromDateTime(occurredAt.UtcDateTime);
        var period = BudgetPeriod.FromDate(occurredDate);

        var budget = await _budgetRepo.GetForCategoryAsync(categoryId.Value, period, ct);
        if (budget is null)
        {
            return;
        }

        var progress = await _progressService.CalculateAsync(budget, occurredDate, ct);
        var tenantIdVo = new TenantId(tenantId);

        var anyAdded = false;

        // Threshold customizable (default 80).
        if (await TryQueueAlertAsync(tenantIdVo, budget, budget.AlertThresholdPercent, progress, ct))
        {
            anyAdded = true;
        }

        // Threshold 100% (sempre — não é configurável).
        if (budget.AlertThresholdPercent != 100)
        {
            if (await TryQueueAlertAsync(tenantIdVo, budget, 100, progress, ct))
            {
                anyAdded = true;
            }
        }

        if (!anyAdded)
        {
            return;
        }

        try
        {
            await _alertRepo.SaveChangesAsync(ct);
        }
        catch (BudgetAlertDuplicateException ex)
        {
            _logger.LogDebug(ex,
                "Budget alert duplicado ignorado (race entre dispatches paralelos) — budget {BudgetId}",
                budget.Id);
        }
    }

    private async Task<bool> TryQueueAlertAsync(
        TenantId tenantId,
        Budget budget,
        int threshold,
        BudgetProgress progress,
        CancellationToken ct)
    {
        if (progress.PercentUsed < threshold)
        {
            return false;
        }

        if (await _alertRepo.ExistsAsync(budget.Id, threshold, ct))
        {
            return false;
        }

        var alert = BudgetAlert.Create(tenantId, budget.Id, threshold, progress.Spent);
        await _alertRepo.AddAsync(alert, ct);

        _logger.LogInformation(
            "Budget alert criado: budget={BudgetId} threshold={Threshold} percentUsed={Percent}",
            budget.Id, threshold, progress.PercentUsed);
        return true;
    }
}
