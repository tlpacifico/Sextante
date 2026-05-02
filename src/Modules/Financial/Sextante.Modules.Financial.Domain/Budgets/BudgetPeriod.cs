using Sextante.Modules.Financial.Domain.Common;

namespace Sextante.Modules.Financial.Domain.Budgets;

/// <summary>
/// Representa um período mensal (ano, mês). Phase 5b suporta apenas
/// mês civil — semanal/trimestral/anual ficam para backlog.
/// VO determinístico, puro, testável sem clock.
/// Modelado como <c>record</c> (heap-allocated) para alinhar com o
/// padrão Money/EF Core OwnsOne — `record struct` ficaria sem
/// parameterless ctor visível e complica materialização EF.
/// </summary>
public sealed record BudgetPeriod
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    /// <summary>EF requer parameterless ctor para reidratação OwnsOne.</summary>
    private BudgetPeriod()
    {
        Year = MinYear;
        Month = 1;
    }

    public BudgetPeriod(int year, int month)
    {
        if (year < MinYear || year > MaxYear)
        {
            throw new BudgetPeriodInvalidException(year, month);
        }

        if (month < 1 || month > 12)
        {
            throw new BudgetPeriodInvalidException(year, month);
        }

        Year = year;
        Month = month;
    }

    public int Year { get; init; }
    public int Month { get; init; }

    public DateOnly Start => new(Year, Month, 1);
    public DateOnly End => Start.AddMonths(1).AddDays(-1);
    public int DaysInMonth => DateTime.DaysInMonth(Year, Month);

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    public BudgetPeriod Next()
    {
        return Month == 12
            ? new BudgetPeriod(Year + 1, 1)
            : new BudgetPeriod(Year, Month + 1);
    }

    public static BudgetPeriod FromDate(DateOnly date) => new(date.Year, date.Month);

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}

public sealed class BudgetPeriodInvalidException : FinancialDomainException
{
    public BudgetPeriodInvalidException(int year, int month)
        : base($"Período inválido: {year}-{month:D2}. Esperado ano em [{BudgetPeriod.MinYear}, {BudgetPeriod.MaxYear}] e mês em [1, 12].")
    {
        Year = year;
        Month = month;
    }

    public int Year { get; }
    public int Month { get; }
}
