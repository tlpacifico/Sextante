using FluentAssertions;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.RecurringRules;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.RecurringRulesSpec;

public sealed class RecurringRuleTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Money Eur100 = new(100m, "EUR");
    private static readonly Guid AccountId = GuidV7.NewId();
    private static readonly Guid CategoryId = GuidV7.NewId();
    private static readonly DateOnly Today = new(2026, 5, 1);

    [Fact]
    public void Create_rejects_empty_description()
    {
        var act = () => RecurringRule.Create(
            "", Eur100, AccountId, null, Frequency.Monthly, 1,
            Today, null, null, Tenant, Today);
        act.Should().Throw<RecurringRuleDescriptionRequiredException>();
    }

    [Fact]
    public void Create_rejects_whitespace_description()
    {
        var act = () => RecurringRule.Create(
            "   ", Eur100, AccountId, null, Frequency.Monthly, 1,
            Today, null, null, Tenant, Today);
        act.Should().Throw<RecurringRuleDescriptionRequiredException>();
    }

    [Fact]
    public void Create_rejects_description_too_long()
    {
        var longDesc = new string('a', RecurringRule.DescriptionMaxLength + 1);
        var act = () => RecurringRule.Create(
            longDesc, Eur100, AccountId, null, Frequency.Monthly, 1,
            Today, null, null, Tenant, Today);
        act.Should().Throw<RecurringRuleDescriptionTooLongException>();
    }

    [Fact]
    public void Create_rejects_zero_amount()
    {
        var act = () => RecurringRule.Create(
            "Test", new Money(0m, "EUR"), AccountId, null, Frequency.Monthly, 1,
            Today, null, null, Tenant, Today);
        act.Should().Throw<RecurringRuleAmountMustBePositiveException>();
    }

    [Fact]
    public void Create_rejects_negative_amount()
    {
        var act = () => RecurringRule.Create(
            "Test", new Money(-10m, "EUR"), AccountId, null, Frequency.Monthly, 1,
            Today, null, null, Tenant, Today);
        act.Should().Throw<RecurringRuleAmountMustBePositiveException>();
    }

    [Fact]
    public void Create_rejects_interval_zero()
    {
        var act = () => RecurringRule.Create(
            "Test", Eur100, AccountId, null, Frequency.Monthly, 0,
            Today, null, null, Tenant, Today);
        act.Should().Throw<RecurringRuleIntervalMustBePositiveException>();
    }

    [Fact]
    public void Create_rejects_negative_interval()
    {
        var act = () => RecurringRule.Create(
            "Test", Eur100, AccountId, null, Frequency.Monthly, -1,
            Today, null, null, Tenant, Today);
        act.Should().Throw<RecurringRuleIntervalMustBePositiveException>();
    }

    [Fact]
    public void Create_rejects_start_after_end()
    {
        var act = () => RecurringRule.Create(
            "Test", Eur100, AccountId, null, Frequency.Monthly, 1,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1), null, Tenant, Today);
        act.Should().Throw<RecurringRuleStartDateAfterEndDateException>();
    }

    [Fact]
    public void Create_sets_next_occurrence_to_start_when_in_future()
    {
        var future = new DateOnly(2026, 10, 1);
        var rule = RecurringRule.Create(
            "Test", Eur100, AccountId, null, Frequency.Monthly, 1,
            future, null, null, Tenant, new DateOnly(2026, 5, 1));

        rule.NextOccurrence.Should().Be(future);
    }

    [Fact]
    public void Create_sets_next_occurrence_to_start_when_today()
    {
        var rule = RecurringRule.Create(
            "Test", Eur100, AccountId, null, Frequency.Monthly, 1,
            Today, null, null, Tenant, Today);

        rule.NextOccurrence.Should().Be(Today);
    }

    [Fact]
    public void Create_advances_next_occurrence_when_start_in_past()
    {
        var past = new DateOnly(2026, 1, 1);
        var rule = RecurringRule.Create(
            "Test", Eur100, AccountId, null, Frequency.Daily, 5,
            past, null, null, Tenant, Today);

        // Start=2026-01-01, Daily interval=5 → 01-06, 01-11, ... 
        // From 01-01 to 05-01 (Today) = 120 days. 120/5 = 24 full intervals.
        // After 24 advances: 2026-01-01 + 24*5 = 2026-05-01.
        // 2026-05-01 <= Today → one more: 2026-05-06.
        rule.NextOccurrence.Should().BeAfter(Today);
        rule.NextOccurrence.Should().Be(new DateOnly(2026, 5, 6));
    }

    [Fact]
    public void AdvanceNextOccurrence_daily_moves_by_interval_days()
    {
        var rule = CreateRule(Frequency.Daily, 3, Today);
        rule.AdvanceNextOccurrence().Should().BeFalse();
        rule.NextOccurrence.Should().Be(Today.AddDays(3));
    }

    [Fact]
    public void AdvanceNextOccurrence_weekly_moves_by_interval_weeks()
    {
        var rule = CreateRule(Frequency.Weekly, 2, Today);
        rule.AdvanceNextOccurrence().Should().BeFalse();
        rule.NextOccurrence.Should().Be(Today.AddDays(14));
    }

    [Fact]
    public void AdvanceNextOccurrence_monthly_interval_1()
    {
        var start = new DateOnly(2026, 1, 15);
        var rule = CreateRule(Frequency.Monthly, 1, start);
        rule.AdvanceNextOccurrence().Should().BeFalse();
        rule.NextOccurrence.Should().Be(new DateOnly(2026, 2, 15));
    }

    [Fact]
    public void AdvanceNextOccurrence_monthly_clamp_short_month()
    {
        var start = new DateOnly(2026, 1, 31);
        var rule = CreateRule(Frequency.Monthly, 1, start);
        rule.AdvanceNextOccurrence().Should().BeFalse();
        rule.NextOccurrence.Should().Be(new DateOnly(2026, 2, 28));

        rule.AdvanceNextOccurrence().Should().BeFalse();
        rule.NextOccurrence.Should().Be(new DateOnly(2026, 3, 28));

        rule.AdvanceNextOccurrence().Should().BeFalse();
        rule.NextOccurrence.Should().Be(new DateOnly(2026, 4, 28));
    }

    [Fact]
    public void AdvanceNextOccurrence_yearly_clamp_feb29()
    {
        var start = new DateOnly(2024, 2, 29);
        var rule = CreateRule(Frequency.Yearly, 1, start);
        rule.AdvanceNextOccurrence().Should().BeFalse();
        rule.NextOccurrence.Should().Be(new DateOnly(2025, 2, 28));
    }

    [Fact]
    public void AdvanceNextOccurrence_becomes_null_when_passes_end_date()
    {
        var start = Today;
        var end = Today.AddDays(5);
        var rule = RecurringRule.Create(
            "Test", Eur100, AccountId, null, Frequency.Daily, 3,
            start, end, null, Tenant, Today);
        // NextOccurrence = Today (start)
        // Advance: Today + 3 = May 4
        rule.AdvanceNextOccurrence().Should().BeFalse();
        rule.NextOccurrence.Should().Be(Today.AddDays(3));

        // Advance: May 4 + 3 = May 7 > end (May 6) → null
        rule.AdvanceNextOccurrence().Should().BeTrue();
        rule.NextOccurrence.Should().BeNull();
    }

    [Fact]
    public void AdvanceNextOccurrence_on_null_returns_true()
    {
        var rule = CreateCompletedRule();
        rule.NextOccurrence.Should().BeNull();
        rule.AdvanceNextOccurrence().Should().BeTrue();
    }

    [Fact]
    public void GetUpcomingOccurrences_returns_requested_count()
    {
        var rule = CreateRule(Frequency.Daily, 1, Today);
        var upcoming = rule.GetUpcomingOccurrences(5);
        upcoming.Should().HaveCount(5);
        upcoming[0].Should().Be(Today);
        upcoming[1].Should().Be(Today.AddDays(1));
    }

    [Fact]
    public void GetUpcomingOccurrences_returns_less_when_end_date_near()
    {
        var end = Today.AddDays(2);
        var rule = RecurringRule.Create(
            "Test", Eur100, AccountId, null, Frequency.Daily, 1,
            Today, end, null, Tenant, Today);
        var upcoming = rule.GetUpcomingOccurrences(10);
        upcoming.Should().HaveCount(3);
    }

    [Fact]
    public void GetUpcomingOccurrences_is_pure_does_not_mutate_rule()
    {
        var rule = CreateRule(Frequency.Daily, 1, Today);
        var original = rule.NextOccurrence;
        rule.GetUpcomingOccurrences(5);
        rule.NextOccurrence.Should().Be(original);
    }

    [Fact]
    public void GetUpcomingOccurrences_returns_empty_when_completed()
    {
        var rule = CreateCompletedRule();
        var upcoming = rule.GetUpcomingOccurrences(5);
        upcoming.Should().BeEmpty();
    }

    [Fact]
    public void Archive_sets_deleted_at()
    {
        var rule = CreateRule(Frequency.Monthly, 1, Today);
        rule.DeletedAt.Should().BeNull();
        rule.Archive();
        rule.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Create_today_stores_tags()
    {
        var rule = RecurringRule.Create(
            "Test", Eur100, AccountId, null, Frequency.Monthly, 1,
            Today, null, new[] { "fixa", "essencial" }, Tenant, Today);
        rule.Tags.Should().BeEquivalentTo(new[] { "fixa", "essencial" });
    }

    [Fact]
    public void Create_is_active_by_default()
    {
        var rule = CreateRule(Frequency.Monthly, 1, Today);
        rule.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Update_recalculates_next_occurrence_when_start_changes()
    {
        var rule = CreateRule(Frequency.Daily, 1, new DateOnly(2026, 1, 1));
        var original = rule.NextOccurrence;

        rule.Update(
            "Test", Eur100, AccountId, null, Frequency.Daily, 1,
            Today, null, true, null, Today);
        rule.NextOccurrence.Should().Be(Today);
        rule.NextOccurrence.Should().NotBe(original);
    }

    [Fact]
    public void Update_recalculates_next_occurrence_when_frequency_changes()
    {
        var rule = CreateRule(Frequency.Daily, 1, Today);
        var original = rule.NextOccurrence;

        rule.Update(
            "Test", Eur100, AccountId, null, Frequency.Weekly, 1,
            Today, null, true, null, Today);
        rule.NextOccurrence.Should().Be(Today);
    }

    [Fact]
    public void Update_does_not_recalculate_when_only_description_changes()
    {
        var rule = CreateRule(Frequency.Monthly, 1, Today);
        var original = rule.NextOccurrence;

        rule.Update(
            "Updated description", Eur100, AccountId, null,
            Frequency.Monthly, 1, Today, null, true, null, Today);
        rule.NextOccurrence.Should().Be(original);
    }

    private static RecurringRule CreateRule(Frequency frequency, int interval, DateOnly startDate)
        => RecurringRule.Create(
            "Test rule", Eur100, AccountId, null, frequency, interval,
            startDate, null, null, Tenant, startDate);

    private static RecurringRule CreateCompletedRule()
    {
        // Start and end both in the past (start < end), so the rule initialises
        // with NextOccurrence = null since start > endDate → completed immediately.
        var past = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 2, 1);
        return RecurringRule.Create(
            "Completed", Eur100, AccountId, null, Frequency.Daily, 1,
            past, end, null, Tenant, Today);
    }
}
