using System.Globalization;
using FluentAssertions;
using Sextante.Modules.Financial.Domain.InstallmentPlans;

namespace Sextante.Modules.Financial.Domain.Tests.InstallmentPlansSpec;

public sealed class InstallmentScheduleTests
{
    private static DateOnly D(string value) => DateOnly.Parse(value, CultureInfo.InvariantCulture);

    [Fact]
    public void Last_installment_absorbs_rounding()
    {
        var schedule = InstallmentSchedule.Build(100m, 3, D("2026-01-10"));

        schedule.Select(i => i.Amount).Should().Equal(33.33m, 33.33m, 33.34m);
        schedule.Select(i => i.Number).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Even_split_has_equal_installments()
    {
        InstallmentSchedule.Build(600m, 6, D("2026-01-10")).Select(i => i.Amount)
            .Should().AllBeEquivalentTo(100m);
    }

    [Fact]
    public void Rounds_down_and_puts_the_rest_in_the_last()
    {
        var schedule = InstallmentSchedule.Build(1000m, 7, D("2026-01-10"));

        schedule.Take(6).Select(i => i.Amount).Should().AllBeEquivalentTo(142.85m);
        schedule[6].Amount.Should().Be(142.90m);
    }

    [Fact]
    public void Sum_always_equals_total()
    {
        foreach (var count in Enumerable.Range(2, 119))
        {
            foreach (var cents in new[] { 1, 7, 99, 100_001 })
            {
                var total = Math.Max(cents, count) * 0.01m;
                var schedule = InstallmentSchedule.Build(total, count, D("2026-01-10"));

                schedule.Should().HaveCount(count);
                schedule.Sum(i => i.Amount).Should().Be(total, $"{total} em {count}");
                schedule.Should().OnlyContain(i => i.Amount > 0m, $"{total} em {count}");
            }
        }
    }

    [Fact]
    public void Dates_are_counted_from_the_first_and_clamped_to_month_end()
    {
        var schedule = InstallmentSchedule.Build(400m, 4, D("2026-01-31"));

        schedule.Select(i => i.Date).Should().Equal(
            D("2026-01-31"), D("2026-02-28"), D("2026-03-31"), D("2026-04-30"));
    }

    [Fact]
    public void Leap_year_february()
    {
        InstallmentSchedule.Build(200m, 2, D("2028-01-31"))[1].Date.Should().Be(D("2028-02-29"));
    }
}
