using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.CategorizationRules;

public sealed class CategorizationRule : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int NameMaxLength = 128;
    public const int PatternMaxLength = 512;

    private CategorizationRule()
    {
        Name = null!;
        Pattern = null!;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public string Pattern { get; private set; }
    public CategorizationMatchType MatchType { get; private set; }
    public RuleAction Action { get; private set; }

    /// <summary><c>null</c> em regras <see cref="RuleAction.MarkAsTransfer"/>.</summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>Conta alvo da transferência; só em <see cref="RuleAction.MarkAsTransfer"/>.</summary>
    public Guid? TargetAccountId { get; private set; }
    public int Priority { get; private set; }
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static CategorizationRule Create(
        string name,
        string pattern,
        CategorizationMatchType matchType,
        Guid categoryId,
        int priority,
        TenantId tenantId)
    {
        ValidateCommon(name, pattern, priority);
        if (categoryId == Guid.Empty)
            throw new CategorizationRuleCategoryRequiredException();

        return New(name, pattern, matchType, RuleAction.SetCategory, categoryId, null, priority, tenantId);
    }

    /// <summary>
    /// Regra que marca a transação como transferência para <paramref name="targetAccountId"/>
    /// (ex.: "PAGAMENTO CARTAO" → cartão de crédito). Sem categoria.
    /// </summary>
    public static CategorizationRule CreateTransfer(
        string name,
        string pattern,
        CategorizationMatchType matchType,
        Guid targetAccountId,
        int priority,
        TenantId tenantId)
    {
        ValidateCommon(name, pattern, priority);
        if (targetAccountId == Guid.Empty)
            throw new CategorizationRuleTargetAccountRequiredException();

        return New(name, pattern, matchType, RuleAction.MarkAsTransfer, null, targetAccountId, priority, tenantId);
    }

    public void Update(
        string name,
        string pattern,
        CategorizationMatchType matchType,
        Guid categoryId,
        int priority,
        bool isActive)
    {
        ValidateCommon(name, pattern, priority);
        if (categoryId == Guid.Empty)
            throw new CategorizationRuleCategoryRequiredException();

        Apply(name, pattern, matchType, RuleAction.SetCategory, categoryId, null, priority, isActive);
    }

    public void UpdateAsTransfer(
        string name,
        string pattern,
        CategorizationMatchType matchType,
        Guid targetAccountId,
        int priority,
        bool isActive)
    {
        ValidateCommon(name, pattern, priority);
        if (targetAccountId == Guid.Empty)
            throw new CategorizationRuleTargetAccountRequiredException();

        Apply(name, pattern, matchType, RuleAction.MarkAsTransfer, null, targetAccountId, priority, isActive);
    }

    public void SetPriority(int priority)
    {
        if (priority < 0)
            throw new CategorizationRulePriorityNegativeException();
        Priority = priority;
    }

    public void Archive() => DeletedAt = DateTimeOffset.UtcNow;

    private static CategorizationRule New(
        string name,
        string pattern,
        CategorizationMatchType matchType,
        RuleAction action,
        Guid? categoryId,
        Guid? targetAccountId,
        int priority,
        TenantId tenantId)
    {
        var rule = new CategorizationRule
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
        };
        rule.Apply(name, pattern, matchType, action, categoryId, targetAccountId, priority, isActive: true);
        return rule;
    }

    private void Apply(
        string name,
        string pattern,
        CategorizationMatchType matchType,
        RuleAction action,
        Guid? categoryId,
        Guid? targetAccountId,
        int priority,
        bool isActive)
    {
        Name = name.Trim();
        Pattern = pattern.Trim();
        MatchType = matchType;
        Action = action;
        CategoryId = categoryId;
        TargetAccountId = targetAccountId;
        Priority = priority;
        IsActive = isActive;
    }

    private static void ValidateCommon(string name, string pattern, int priority)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new CategorizationRuleNameRequiredException();
        if (name.Length > NameMaxLength)
            throw new CategorizationRuleNameTooLongException(NameMaxLength);
        if (string.IsNullOrWhiteSpace(pattern))
            throw new CategorizationRulePatternRequiredException();
        if (pattern.Length > PatternMaxLength)
            throw new CategorizationRulePatternTooLongException(PatternMaxLength);
        if (priority < 0)
            throw new CategorizationRulePriorityNegativeException();
    }
}
