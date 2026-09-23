using System.Globalization;
using FluentAssertions;
using Sextante.Modules.Financial.Domain.Accounts;

namespace Sextante.Modules.Financial.Domain.Tests.AccountsSpec;

public sealed class CreditCardCalendarTests
{
    private static DateOnly D(string value) => DateOnly.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(2026, 2, 31, "2026-03-01")]
    [InlineData(2026, 4, 31, "2026-05-01")]
    [InlineData(2026, 3, 31, "2026-03-31")]
    [InlineData(2026, 2, 30, "2026-03-01")]
    [InlineData(2028, 2, 29, "2028-02-29")]
    [InlineData(2026, 2, 29, "2026-03-01")]
    [InlineData(2026, 9, 20, "2026-09-20")]
    [InlineData(2026, 12, 31, "2026-12-31")]
    public void ClosingDate_uses_first_day_of_next_month_when_day_does_not_exist(
        int year, int month, int closingDay, string expected)
    {
        CreditCardCalendar.ClosingDate(year, month, closingDay).Should().Be(D(expected));
    }

    [Theory]
    [InlineData(2026, 9, 20, 10, "2026-10-10")]
    [InlineData(2026, 9, 5, 25, "2026-09-25")]
    [InlineData(2026, 2, 31, 10, "2026-03-10")]
    [InlineData(2026, 1, 20, 31, "2026-01-31")]
    [InlineData(2026, 1, 25, 30, "2026-01-30")]
    [InlineData(2026, 2, 28, 31, "2026-03-01")]
    [InlineData(2026, 12, 20, 10, "2027-01-10")]
    [InlineData(2026, 9, 20, 20, "2026-10-20")]
    public void PaymentDueDate_is_next_month_when_due_day_not_after_closing_day(
        int year, int month, int closingDay, int dueDay, string expected)
    {
        CreditCardCalendar.PaymentDueDate(year, month, closingDay, dueDay).Should().Be(D(expected));
    }

    [Theory]
    [InlineData("2026-09-23", 20, 2026, 10, "2026-09-21", "2026-10-20")]
    [InlineData("2026-09-20", 20, 2026, 9, "2026-08-21", "2026-09-20")]
    [InlineData("2026-09-21", 20, 2026, 10, "2026-09-21", "2026-10-20")]
    [InlineData("2026-03-01", 31, 2026, 2, "2026-02-01", "2026-03-01")]
    [InlineData("2026-03-02", 31, 2026, 3, "2026-03-02", "2026-03-31")]
    [InlineData("2026-01-05", 20, 2026, 1, "2025-12-21", "2026-01-20")]
    [InlineData("2026-12-25", 20, 2027, 1, "2026-12-21", "2027-01-20")]
    public void CycleContaining_finds_the_open_cycle(
        string date, int closingDay, int year, int month, string start, string end)
    {
        var cycle = CreditCardCalendar.CycleContaining(D(date), closingDay, 10);

        cycle.Year.Should().Be(year);
        cycle.Month.Should().Be(month);
        cycle.Start.Should().Be(D(start));
        cycle.End.Should().Be(D(end));
    }

    [Fact]
    public void Previous_cycle_crosses_the_year()
    {
        var current = CreditCardCalendar.Cycle(2026, 1, 20, 10);

        var previous = CreditCardCalendar.Previous(current, 20, 10);

        previous.Year.Should().Be(2025);
        previous.Month.Should().Be(12);
        previous.Start.Should().Be(D("2025-11-21"));
        previous.End.Should().Be(D("2025-12-20"));
        previous.PaymentDueDate.Should().Be(D("2026-01-10"));
    }

    [Fact]
    public void Consecutive_cycles_have_no_gaps_or_overlaps()
    {
        foreach (var year in new[] { 2026, 2028 })
        {
            for (var closingDay = 1; closingDay <= 31; closingDay++)
            {
                for (var month = 1; month <= 12; month++)
                {
                    var cycle = CreditCardCalendar.Cycle(year, month, closingDay, 10);
                    var previousMonth = new DateOnly(year, month, 1).AddMonths(-1);
                    var previous = CreditCardCalendar.Cycle(previousMonth.Year, previousMonth.Month, closingDay, 10);

                    cycle.Start.Should().Be(previous.End.AddDays(1), $"fecho {closingDay}, {year}-{month}");
                    cycle.Start.Should().BeOnOrBefore(cycle.End, $"fecho {closingDay}, {year}-{month}");
                }
            }
        }
    }

    [Fact]
    public void Every_date_belongs_to_the_cycle_that_CycleContaining_returns()
    {
        for (var closingDay = 1; closingDay <= 31; closingDay++)
        {
            for (var date = D("2027-12-01"); date <= D("2028-04-30"); date = date.AddDays(1))
            {
                var cycle = CreditCardCalendar.CycleContaining(date, closingDay, 10);

                date.Should().BeOnOrAfter(cycle.Start, $"fecho {closingDay}, {date}");
                date.Should().BeOnOrBefore(cycle.End, $"fecho {closingDay}, {date}");
            }
        }
    }
}
