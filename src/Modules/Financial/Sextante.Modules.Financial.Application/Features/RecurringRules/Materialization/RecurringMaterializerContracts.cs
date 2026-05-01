namespace Sextante.Modules.Financial.Application.Features.RecurringRules.Materialization;

public sealed record RecurringMaterializerPayload(DateOnly RunDate);

public sealed record RecurringMaterializerResult(
    int TotalRulesProcessed,
    int Materialized,
    int SkippedDuplicate,
    int SkippedNoRate,
    int Completed);
