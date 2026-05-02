using FluentAssertions;
using Sextante.Modules.Financial.Domain.Budgets;

namespace Sextante.Modules.Financial.Domain.Tests.Budgets;

public sealed class BudgetPeriodTests
{
    [Fact]
    public void Ctor_rejects_year_below_min()
    {
        var act = () => new BudgetPeriod(1999, 5);
        act.Should().Throw<BudgetPeriodInvalidException>();
    }

    [Fact]
    public void Ctor_rejects_year_above_max()
    {
        var act = () => new BudgetPeriod(2101, 5);
        act.Should().Throw<BudgetPeriodInvalidException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Ctor_rejects_invalid_month(int month)
    {
        var act = () => new BudgetPeriod(2026, month);
        act.Should().Throw<BudgetPeriodInvalidException>();
    }

    [Fact]
    public void Start_and_end_correct_for_january()
    {
        var p = new BudgetPeriod(2026, 1);
        p.Start.Should().Be(new DateOnly(2026, 1, 1));
        p.End.Should().Be(new DateOnly(2026, 1, 31));
        p.DaysInMonth.Should().Be(31);
    }

    [Fact]
    public void Start_and_end_correct_for_december()
    {
        var p = new BudgetPeriod(2026, 12);
        p.Start.Should().Be(new DateOnly(2026, 12, 1));
        p.End.Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public void DaysInMonth_handles_february_non_leap()
    {
        new BudgetPeriod(2026, 2).DaysInMonth.Should().Be(28);
    }

    [Fact]
    public void DaysInMonth_handles_february_leap()
    {
        new BudgetPeriod(2024, 2).DaysInMonth.Should().Be(29);
    }

    [Fact]
    public void Contains_handles_boundary_days()
    {
        var p = new BudgetPeriod(2026, 5);
        p.Contains(new DateOnly(2026, 5, 1)).Should().BeTrue();
        p.Contains(new DateOnly(2026, 5, 31)).Should().BeTrue();
        p.Contains(new DateOnly(2026, 4, 30)).Should().BeFalse();
        p.Contains(new DateOnly(2026, 6, 1)).Should().BeFalse();
    }

    [Fact]
    public void Next_advances_within_year()
    {
        new BudgetPeriod(2026, 5).Next().Should().Be(new BudgetPeriod(2026, 6));
    }

    [Fact]
    public void Next_advances_december_to_january_next_year()
    {
        new BudgetPeriod(2026, 12).Next().Should().Be(new BudgetPeriod(2027, 1));
    }

    [Fact]
    public void FromDate_extracts_year_and_month()
    {
        BudgetPeriod.FromDate(new DateOnly(2026, 5, 15)).Should().Be(new BudgetPeriod(2026, 5));
    }
}
