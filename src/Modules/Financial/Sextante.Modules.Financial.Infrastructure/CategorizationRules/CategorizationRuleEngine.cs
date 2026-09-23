using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Domain.CategorizationRules;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Infrastructure.CategorizationRules;

public sealed class CategorizationRuleEngine : ICategorizationRuleEngine
{
    private readonly ICategorizationRuleRepository _repository;

    public CategorizationRuleEngine(ICategorizationRuleRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<CategorizationMatchResult>> ApplyAsync(
        IReadOnlyList<TransactionToCategorize> transactions,
        TenantId tenantId,
        CancellationToken ct)
    {
        var rules = await _repository.ListActiveAsync(ct);

        var results = new List<CategorizationMatchResult>();

        foreach (var tx in transactions)
        {
            if (string.IsNullOrWhiteSpace(tx.Description))
            {
                results.Add(new CategorizationMatchResult(tx.TransactionId, null, null));
                continue;
            }

            CategorizationMatchResult? match = null;
            foreach (var rule in rules)
            {
                if (Matches(rule, tx.Description))
                {
                    match = new CategorizationMatchResult(
                        tx.TransactionId,
                        rule.Id,
                        rule.Action == RuleAction.SetCategory ? rule.CategoryId : null,
                        rule.Action == RuleAction.MarkAsTransfer ? rule.TargetAccountId : null);
                    break;
                }
            }

            results.Add(match ?? new CategorizationMatchResult(tx.TransactionId, null, null));
        }

        return results;
    }

    private static bool Matches(CategorizationRule rule, string description)
    {
        return rule.MatchType switch
        {
            CategorizationMatchType.Contains => description.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            CategorizationMatchType.Equals => description.Equals(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            CategorizationMatchType.StartsWith => description.StartsWith(rule.Pattern, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
