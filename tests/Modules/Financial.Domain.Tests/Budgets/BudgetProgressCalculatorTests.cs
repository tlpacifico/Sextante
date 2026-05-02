using FluentAssertions;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.Budgets;

public sealed class BudgetProgressCalculatorTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid CategoryId = GuidV7.NewId();
    private static readonly BudgetPeriod May2026 = new(2026, 5);

    [Fact]
    public void Empty_contributions_returns_zero_spent()
    {
        var budget = NewBudget(500m);
        var progress = BudgetProgressCalculator.Calculate(
            budget, Array.Empty<TransactionContribution>(), new DateOnly(2026, 5, 10));

        progress.Spent.Amount.Should().Be(0m);
        progress.Remaining.Amount.Should().Be(500m);
        progress.PercentUsed.Should().Be(0m);
        progress.HasIncompleteRates.Should().BeFalse();
    }

    [Fact]
    public void Spent_below_limit_yields_positive_remaining()
    {
        var budget = NewBudget(500m);
        var contributions = new[]
        {
            new TransactionContribution(100m, false, new DateOnly(2026, 5, 5)),
            new TransactionContribution(150m, false, new DateOnly(2026, 5, 9)),
        };

        var progress = BudgetProgressCalculator.Calculate(
            budget, contributions, new DateOnly(2026, 5, 10));

        progress.Spent.Amount.Should().Be(250m);
        progress.Remaining.Amount.Should().Be(250m);
        progress.PercentUsed.Should().Be(50m);
    }

    [Fact]
    public void Spent_equals_limit_returns_100_percent_zero_remaining()
    {
        var budget = NewBudget(500m);
        var contributions = new[]
        {
            new TransactionContribution(500m, false, new DateOnly(2026, 5, 5)),
        };

        var progress = BudgetProgressCalculator.Calculate(
            budget, contributions, new DateOnly(2026, 5, 10));

        progress.Remaining.Amount.Should().Be(0m);
        progress.PercentUsed.Should().Be(100m);
    }

    [Fact]
    public void Spent_above_limit_yields_negative_remaining_and_over_100_percent()
    {
        var budget = NewBudget(500m);
        var contributions = new[]
        {
            new TransactionContribution(550m, false, new DateOnly(2026, 5, 5)),
        };

        var progress = BudgetProgressCalculator.Calculate(
            budget, contributions, new DateOnly(2026, 5, 10));

        progress.Remaining.Amount.Should().Be(-50m);
        progress.PercentUsed.Should().Be(110m);
    }

    [Fact]
    public void Days_elapsed_below_minimum_returns_null_projection()
    {
        var budget = NewBudget(500m);
        var contributions = new[]
        {
            new TransactionContribution(100m, false, new DateOnly(2026, 5, 1)),
        };

        // Day 4 of period — below MinDaysForProjection (5).
        var progress = BudgetProgressCalculator.Calculate(
            budget, contributions, new DateOnly(2026, 5, 4));
        progress.ProjectedEndOfPeriod.Should().BeNull();
    }

    [Fact]
    public void Days_elapsed_at_or_above_minimum_projects_end_of_period()
    {
        var budget = NewBudget(500m);
        var contributions = new[]
        {
            new TransactionContribution(100m, false, new DateOnly(2026, 5, 5)),
        };

        // Day 10, May has 31 days → projected = (100 / 10) * 31 = 310.
        var progress = BudgetProgressCalculator.Calculate(
            budget, contributions, new DateOnly(2026, 5, 10));

        progress.ProjectedEndOfPeriod.Should().NotBeNull();
        progress.ProjectedEndOfPeriod!.Amount.Should().Be(310m);
    }

    [Fact]
    public void Rate_missing_contribution_is_excluded_from_spent_and_flags_incomplete()
    {
        var budget = NewBudget(500m);
        var contributions = new[]
        {
            new TransactionContribution(200m, false, new DateOnly(2026, 5, 5)),
            new TransactionContribution(0m, true, new DateOnly(2026, 5, 6)),
        };

        var progress = BudgetProgressCalculator.Calculate(
            budget, contributions, new DateOnly(2026, 5, 10));

        progress.Spent.Amount.Should().Be(200m);
        progress.HasIncompleteRates.Should().BeTrue();
    }

    [Fact]
    public void When_today_after_period_end_no_projection()
    {
        var budget = NewBudget(500m);
        var contributions = new[]
        {
            new TransactionContribution(400m, false, new DateOnly(2026, 5, 15)),
        };

        var progress = BudgetProgressCalculator.Calculate(
            budget, contributions, new DateOnly(2026, 6, 5));

        progress.ProjectedEndOfPeriod.Should().BeNull();
        progress.Spent.Amount.Should().Be(400m);
    }

    private static Budget NewBudget(decimal limit)
        => Budget.Create(Tenant, CategoryId, May2026, new Money(limit, "EUR"), 80, null);
}
