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

/// <summary>
/// <see cref="NewCategoryId"/> vem de regras SetCategory;
/// <see cref="TargetAccountId"/> de regras MarkAsTransfer (Phase 6.5) — nunca os dois.
/// </summary>
public sealed record CategorizationMatchResult(
    Guid TransactionId,
    Guid? MatchedRuleId,
    Guid? NewCategoryId,
    Guid? TargetAccountId = null);
