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
    public Guid CategoryId { get; private set; }
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
        if (string.IsNullOrWhiteSpace(name))
            throw new CategorizationRuleNameRequiredException();
        if (name.Length > NameMaxLength)
            throw new CategorizationRuleNameTooLongException(NameMaxLength);
        if (string.IsNullOrWhiteSpace(pattern))
            throw new CategorizationRulePatternRequiredException();
        if (pattern.Length > PatternMaxLength)
            throw new CategorizationRulePatternTooLongException(PatternMaxLength);
        if (categoryId == Guid.Empty)
            throw new CategorizationRuleCategoryRequiredException();
        if (priority < 0)
            throw new CategorizationRulePriorityNegativeException();

        return new CategorizationRule
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            Name = name.Trim(),
            Pattern = pattern.Trim(),
            MatchType = matchType,
            CategoryId = categoryId,
            Priority = priority,
            IsActive = true,
        };
    }

    public void Update(
        string name,
        string pattern,
        CategorizationMatchType matchType,
        Guid categoryId,
        int priority,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new CategorizationRuleNameRequiredException();
        if (name.Length > NameMaxLength)
            throw new CategorizationRuleNameTooLongException(NameMaxLength);
        if (string.IsNullOrWhiteSpace(pattern))
            throw new CategorizationRulePatternRequiredException();
        if (pattern.Length > PatternMaxLength)
            throw new CategorizationRulePatternTooLongException(PatternMaxLength);
        if (categoryId == Guid.Empty)
            throw new CategorizationRuleCategoryRequiredException();
        if (priority < 0)
            throw new CategorizationRulePriorityNegativeException();

        Name = name.Trim();
        Pattern = pattern.Trim();
        MatchType = matchType;
        CategoryId = categoryId;
        Priority = priority;
        IsActive = isActive;
    }

    public void SetPriority(int priority)
    {
        if (priority < 0)
            throw new CategorizationRulePriorityNegativeException();
        Priority = priority;
    }

    public void Archive() => DeletedAt = DateTimeOffset.UtcNow;
}
