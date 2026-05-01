using Sextante.Modules.Financial.Domain.RecurringRules;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.RecurringRules;

public sealed record RecurringRuleResponse(
    Guid Id,
    string Description,
    Money Amount,
    Guid AccountId,
    Guid? CategoryId,
    string Frequency,
    int Interval,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? NextOccurrence,
    bool IsActive,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateRecurringRuleCommand(
    string Description,
    decimal Amount,
    string Currency,
    Guid AccountId,
    Guid? CategoryId,
    string Frequency,
    int Interval,
    DateOnly StartDate,
    DateOnly? EndDate,
    IReadOnlyList<string>? Tags);

public sealed record UpdateRecurringRuleCommand(
    Guid Id,
    string Description,
    decimal Amount,
    string Currency,
    Guid AccountId,
    Guid? CategoryId,
    string Frequency,
    int Interval,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsActive,
    IReadOnlyList<string>? Tags);

public sealed record ArchiveRecurringRuleCommand(Guid Id);

public sealed record GetRecurringRuleByIdQuery(Guid Id);

public sealed record ListRecurringRulesQuery();

public sealed record GetUpcomingOccurrencesQuery(Guid RuleId, int Count = 10);
