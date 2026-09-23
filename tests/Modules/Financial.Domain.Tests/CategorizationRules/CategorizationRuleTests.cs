using FluentAssertions;
using Sextante.Modules.Financial.Domain.CategorizationRules;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.CategorizationRulesSpec;

public sealed class CategorizationRuleTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid Category = Guid.NewGuid();

    [Fact]
    public void Create_succeeds_with_valid_inputs()
    {
        var rule = CategorizationRule.Create(
            "Continente", "CONTINENTE", CategorizationMatchType.Contains, Category, priority: 10, Tenant);

        rule.Name.Should().Be("Continente");
        rule.Pattern.Should().Be("CONTINENTE");
        rule.MatchType.Should().Be(CategorizationMatchType.Contains);
        rule.CategoryId.Should().Be(Category);
        rule.Priority.Should().Be(10);
        rule.IsActive.Should().BeTrue();
        rule.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_name(string name)
    {
        var act = () => CategorizationRule.Create(name, "x", CategorizationMatchType.Contains, Category, 0, Tenant);
        act.Should().Throw<CategorizationRuleNameRequiredException>();
    }

    [Fact]
    public void Create_rejects_name_above_max_length()
    {
        var name = new string('a', CategorizationRule.NameMaxLength + 1);
        var act = () => CategorizationRule.Create(name, "x", CategorizationMatchType.Contains, Category, 0, Tenant);
        act.Should().Throw<CategorizationRuleNameTooLongException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_pattern(string pattern)
    {
        var act = () => CategorizationRule.Create("Continente", pattern, CategorizationMatchType.Contains, Category, 0, Tenant);
        act.Should().Throw<CategorizationRulePatternRequiredException>();
    }

    [Fact]
    public void Create_rejects_pattern_above_max_length()
    {
        var pattern = new string('a', CategorizationRule.PatternMaxLength + 1);
        var act = () => CategorizationRule.Create("Continente", pattern, CategorizationMatchType.Contains, Category, 0, Tenant);
        act.Should().Throw<CategorizationRulePatternTooLongException>();
    }

    [Fact]
    public void Create_rejects_default_category_id()
    {
        var act = () => CategorizationRule.Create("Continente", "x", CategorizationMatchType.Contains, Guid.Empty, 0, Tenant);
        act.Should().Throw<CategorizationRuleCategoryRequiredException>();
    }

    [Fact]
    public void Create_rejects_negative_priority()
    {
        var act = () => CategorizationRule.Create("Continente", "x", CategorizationMatchType.Contains, Category, -1, Tenant);
        act.Should().Throw<CategorizationRulePriorityNegativeException>();
    }

    [Fact]
    public void Create_accepts_priority_zero()
    {
        var rule = CategorizationRule.Create("Continente", "x", CategorizationMatchType.Contains, Category, 0, Tenant);
        rule.Priority.Should().Be(0);
    }

    [Fact]
    public void Create_trims_name_and_pattern()
    {
        var rule = CategorizationRule.Create("  Continente  ", "  cont  ", CategorizationMatchType.Contains, Category, 0, Tenant);
        rule.Name.Should().Be("Continente");
        rule.Pattern.Should().Be("cont");
    }

    [Fact]
    public void Update_rewrites_state()
    {
        var rule = CategorizationRule.Create("Old", "old", CategorizationMatchType.Contains, Category, 0, Tenant);
        var newCategory = Guid.NewGuid();

        rule.Update("New", "new", CategorizationMatchType.Equals, newCategory, priority: 5, isActive: false);

        rule.Name.Should().Be("New");
        rule.Pattern.Should().Be("new");
        rule.MatchType.Should().Be(CategorizationMatchType.Equals);
        rule.CategoryId.Should().Be(newCategory);
        rule.Priority.Should().Be(5);
        rule.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Update_rejects_negative_priority()
    {
        var rule = CategorizationRule.Create("Continente", "x", CategorizationMatchType.Contains, Category, 0, Tenant);
        var act = () => rule.Update("Continente", "x", CategorizationMatchType.Contains, Category, -1, true);
        act.Should().Throw<CategorizationRulePriorityNegativeException>();
    }

    [Fact]
    public void SetPriority_rejects_negative()
    {
        var rule = CategorizationRule.Create("Continente", "x", CategorizationMatchType.Contains, Category, 0, Tenant);
        var act = () => rule.SetPriority(-1);
        act.Should().Throw<CategorizationRulePriorityNegativeException>();
    }

    [Fact]
    public void Archive_sets_deleted_at()
    {
        var rule = CategorizationRule.Create("Continente", "x", CategorizationMatchType.Contains, Category, 0, Tenant);
        rule.DeletedAt.Should().BeNull();
        rule.Archive();
        rule.DeletedAt.Should().NotBeNull();
    }

    private static readonly Guid Target = Guid.NewGuid();

    [Fact]
    public void Create_sets_SetCategory_action()
    {
        var rule = CategorizationRule.Create("Continente", "x", CategorizationMatchType.Contains, Category, 0, Tenant);

        rule.Action.Should().Be(RuleAction.SetCategory);
        rule.TargetAccountId.Should().BeNull();
    }

    [Fact]
    public void CreateTransfer_sets_action_and_target_without_category()
    {
        var rule = CategorizationRule.CreateTransfer(
            "Pagamento cartão", "PAGAMENTO CARTAO", CategorizationMatchType.Contains, Target, 3, Tenant);

        rule.Action.Should().Be(RuleAction.MarkAsTransfer);
        rule.TargetAccountId.Should().Be(Target);
        rule.CategoryId.Should().BeNull();
        rule.IsActive.Should().BeTrue();
        rule.Priority.Should().Be(3);
    }

    [Fact]
    public void CreateTransfer_requires_target_account()
    {
        var act = () => CategorizationRule.CreateTransfer(
            "Pagamento cartão", "x", CategorizationMatchType.Contains, Guid.Empty, 0, Tenant);

        act.Should().Throw<CategorizationRuleTargetAccountRequiredException>();
    }

    [Fact]
    public void CreateTransfer_validates_name_like_Create()
    {
        var act = () => CategorizationRule.CreateTransfer(
            " ", "x", CategorizationMatchType.Contains, Target, 0, Tenant);

        act.Should().Throw<CategorizationRuleNameRequiredException>();
    }

    [Fact]
    public void UpdateAsTransfer_switches_a_category_rule_to_transfer()
    {
        var rule = CategorizationRule.Create("Continente", "x", CategorizationMatchType.Contains, Category, 0, Tenant);

        rule.UpdateAsTransfer("Cartão", "CARTAO", CategorizationMatchType.StartsWith, Target, 2, isActive: false);

        rule.Action.Should().Be(RuleAction.MarkAsTransfer);
        rule.TargetAccountId.Should().Be(Target);
        rule.CategoryId.Should().BeNull();
        rule.Name.Should().Be("Cartão");
        rule.MatchType.Should().Be(CategorizationMatchType.StartsWith);
        rule.IsActive.Should().BeFalse();
    }

    [Fact]
    public void UpdateAsTransfer_requires_target_account()
    {
        var rule = CategorizationRule.Create("Continente", "x", CategorizationMatchType.Contains, Category, 0, Tenant);

        var act = () => rule.UpdateAsTransfer("Cartão", "x", CategorizationMatchType.Contains, Guid.Empty, 0, true);

        act.Should().Throw<CategorizationRuleTargetAccountRequiredException>();
    }

    [Fact]
    public void Update_switches_a_transfer_rule_back_to_category()
    {
        var rule = CategorizationRule.CreateTransfer("Cartão", "x", CategorizationMatchType.Contains, Target, 0, Tenant);

        rule.Update("Continente", "x", CategorizationMatchType.Contains, Category, 0, true);

        rule.Action.Should().Be(RuleAction.SetCategory);
        rule.CategoryId.Should().Be(Category);
        rule.TargetAccountId.Should().BeNull();
    }
}
