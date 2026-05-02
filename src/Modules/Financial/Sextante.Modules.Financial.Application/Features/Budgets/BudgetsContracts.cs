namespace Sextante.Modules.Financial.Application.Features.Budgets;

public sealed record BudgetProgressResponse(
    decimal LimitAmount,
    string LimitCurrency,
    decimal SpentAmount,
    decimal RemainingAmount,
    decimal PercentUsed,
    decimal? ProjectedAmount,
    bool HasIncompleteRates);

public sealed record BudgetResponse(
    Guid Id,
    Guid CategoryId,
    int Year,
    int Month,
    decimal LimitAmount,
    string LimitCurrency,
    int AlertThresholdPercent,
    string? Notes,
    BudgetProgressResponse Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record BudgetAlertResponse(
    Guid Id,
    Guid BudgetId,
    Guid CategoryId,
    int Threshold,
    DateTimeOffset TriggeredAt,
    decimal SpentAtTriggerAmount,
    string SpentAtTriggerCurrency,
    bool Acknowledged,
    DateTimeOffset? AcknowledgedAt);

public sealed record CreateBudgetCommand(
    Guid CategoryId,
    int Year,
    int Month,
    decimal LimitAmount,
    string LimitCurrency,
    int? AlertThresholdPercent,
    string? Notes);

public sealed record UpdateBudgetCommand(
    Guid Id,
    decimal LimitAmount,
    string LimitCurrency,
    int? AlertThresholdPercent,
    string? Notes);

public sealed record ArchiveBudgetCommand(Guid Id);

public sealed record GetBudgetByIdQuery(Guid Id);

public sealed record ListBudgetsQuery(int? Year, int? Month);

public sealed record GetBudgetProgressQuery(Guid Id);

public sealed record ListActiveBudgetAlertsQuery();

public sealed record AcknowledgeBudgetAlertCommand(Guid Id);
