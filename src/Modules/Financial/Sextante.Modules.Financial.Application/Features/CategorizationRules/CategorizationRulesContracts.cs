using Sextante.Modules.Financial.Domain.CategorizationRules;

namespace Sextante.Modules.Financial.Application.Features.CategorizationRules;

public sealed record CategorizationRuleResponse(
    Guid Id,
    string Name,
    string Pattern,
    string MatchType,
    Guid CategoryId,
    string CategoryName,
    string CategoryIcon,
    string CategoryColor,
    int Priority,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateCategorizationRuleCommand(
    string Name,
    string Pattern,
    string MatchType,
    Guid CategoryId,
    int Priority);

public sealed record UpdateCategorizationRuleCommand(
    Guid Id,
    string Name,
    string Pattern,
    string MatchType,
    Guid CategoryId,
    int Priority,
    bool IsActive);

public sealed record ArchiveCategorizationRuleCommand(Guid Id);

public sealed record GetCategorizationRuleByIdQuery(Guid Id);

public sealed record ListCategorizationRulesQuery();

public sealed record ReorderCategorizationRulesCommand(IReadOnlyList<Guid> RuleIds);

public sealed record ReapplyCategorizationRulesCommand(
    Guid? CategoryId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    bool OnlyUncategorized);

public sealed record ReapplyCategorizationRulesResponse(
    int TotalProcessed,
    int CategorizedCount,
    int UnchangedCount);
