using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.CategorizationRules;

public interface ICategorizationRuleEngine
{
    Task<IReadOnlyList<CategorizationMatchResult>> ApplyAsync(
        IReadOnlyList<TransactionToCategorize> transactions,
        TenantId tenantId,
        CancellationToken ct);
}

public sealed record TransactionToCategorize(
    Guid TransactionId,
    string Description);

public sealed record CategorizationMatchResult(
    Guid TransactionId,
    Guid? MatchedRuleId,
    Guid? NewCategoryId);
