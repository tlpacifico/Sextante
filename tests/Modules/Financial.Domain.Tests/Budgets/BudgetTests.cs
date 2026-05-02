using FluentAssertions;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.Budgets;

public sealed class BudgetTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid CategoryId = GuidV7.NewId();
    private static readonly BudgetPeriod May2026 = new(2026, 5);

    [Fact]
    public void Create_with_valid_inputs_succeeds()
    {
        var budget = Budget.Create(
            Tenant, CategoryId, May2026,
            new Money(500m, "EUR"),
            alertThresholdPercent: 80,
            notes: "Renda + condomínio");

        budget.Id.Should().NotBe(Guid.Empty);
        budget.TenantId.Should().Be(Tenant);
        budget.CategoryId.Should().Be(CategoryId);
        budget.Period.Should().Be(May2026);
        budget.Limit.Should().Be(new Money(500m, "EUR"));
        budget.AlertThresholdPercent.Should().Be(80);
        budget.Notes.Should().Be("Renda + condomínio");
        budget.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_with_null_threshold_uses_default_80()
    {
        var budget = Budget.Create(
            Tenant, CategoryId, May2026, new Money(100m, "EUR"),
            alertThresholdPercent: null, notes: null);
        budget.AlertThresholdPercent.Should().Be(Budget.DefaultThreshold);
    }

    [Fact]
    public void Create_rejects_zero_limit()
    {
        var act = () => Budget.Create(
            Tenant, CategoryId, May2026, new Money(0m, "EUR"), null, null);
        act.Should().Throw<BudgetLimitMustBePositiveException>();
    }

    [Fact]
    public void Create_rejects_negative_limit()
    {
        var act = () => Budget.Create(
            Tenant, CategoryId, May2026, new Money(-1m, "EUR"), null, null);
        act.Should().Throw<BudgetLimitMustBePositiveException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-5)]
    public void Create_rejects_invalid_threshold(int threshold)
    {
        var act = () => Budget.Create(
            Tenant, CategoryId, May2026, new Money(100m, "EUR"), threshold, null);
        act.Should().Throw<BudgetThresholdInvalidException>();
    }

    [Fact]
    public void Create_rejects_notes_too_long()
    {
        var notes = new string('a', Budget.NotesMaxLength + 1);
        var act = () => Budget.Create(
            Tenant, CategoryId, May2026, new Money(100m, "EUR"), null, notes);
        act.Should().Throw<BudgetNotesTooLongException>();
    }

    [Fact]
    public void Create_normalizes_blank_notes_to_null()
    {
        var budget = Budget.Create(
            Tenant, CategoryId, May2026, new Money(100m, "EUR"), null, "   ");
        budget.Notes.Should().BeNull();
    }

    [Fact]
    public void UpdateLimit_changes_limit_when_positive()
    {
        var budget = NewBudget();
        budget.UpdateLimit(new Money(700m, "EUR"));
        budget.Limit.Amount.Should().Be(700m);
    }

    [Fact]
    public void UpdateLimit_rejects_zero()
    {
        var budget = NewBudget();
        var act = () => budget.UpdateLimit(new Money(0m, "EUR"));
        act.Should().Throw<BudgetLimitMustBePositiveException>();
    }

    [Fact]
    public void UpdateThreshold_accepts_valid_range()
    {
        var budget = NewBudget();
        budget.UpdateThreshold(50);
        budget.AlertThresholdPercent.Should().Be(50);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void UpdateThreshold_rejects_out_of_range(int t)
    {
        var budget = NewBudget();
        var act = () => budget.UpdateThreshold(t);
        act.Should().Throw<BudgetThresholdInvalidException>();
    }

    [Fact]
    public void UpdateNotes_normalizes_and_persists()
    {
        var budget = NewBudget();
        budget.UpdateNotes("  novo  ");
        budget.Notes.Should().Be("novo");

        budget.UpdateNotes(null);
        budget.Notes.Should().BeNull();

        budget.UpdateNotes("   ");
        budget.Notes.Should().BeNull();
    }

    [Fact]
    public void UpdateNotes_rejects_too_long()
    {
        var budget = NewBudget();
        var act = () => budget.UpdateNotes(new string('a', Budget.NotesMaxLength + 1));
        act.Should().Throw<BudgetNotesTooLongException>();
    }

    [Fact]
    public void Archive_sets_DeletedAt()
    {
        var budget = NewBudget();
        budget.Archive();
        budget.DeletedAt.Should().NotBeNull();
    }

    private static Budget NewBudget()
        => Budget.Create(Tenant, CategoryId, May2026, new Money(500m, "EUR"), 80, null);
}
