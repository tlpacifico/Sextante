namespace Sextante.Modules.Financial.Domain.Accounts;

/// <summary>
/// Ciclo nominal (<see cref="Year"/>, <see cref="Month"/>) de um cartão:
/// do dia a seguir ao fecho do mês anterior até ao fecho deste mês, datas
/// inclusivas; <see cref="PaymentDueDate"/> é o dia de pagamento do extrato
/// que fecha em <see cref="End"/>.
/// </summary>
public sealed record CreditCardCycle(int Year, int Month, DateOnly Start, DateOnly End, DateOnly PaymentDueDate);

/// <summary>
/// Datas de fecho e pagamento de um cartão (Phase 6.5 grupo 5). Regras
/// decididas com o utilizador: um dia que não existe no mês (ex.: 31 em
/// fevereiro) passa para o dia 1 do mês seguinte; o pagamento é no mês a
/// seguir ao fecho quando o dia de pagamento é ≤ ao dia de fecho, senão no
/// próprio mês — sempre contado a partir do mês nominal do ciclo.
/// </summary>
public static class CreditCardCalendar
{
    public static DateOnly DayOrNextMonthStart(int year, int month, int day)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        return day <= daysInMonth
            ? new DateOnly(year, month, day)
            : new DateOnly(year, month, 1).AddMonths(1);
    }

    public static DateOnly ClosingDate(int year, int month, int closingDay)
        => DayOrNextMonthStart(year, month, closingDay);

    public static DateOnly PaymentDueDate(int year, int month, int closingDay, int dueDay)
    {
        var nominal = new DateOnly(year, month, 1);
        var dueMonth = dueDay <= closingDay ? nominal.AddMonths(1) : nominal;
        return DayOrNextMonthStart(dueMonth.Year, dueMonth.Month, dueDay);
    }

    public static CreditCardCycle Cycle(int year, int month, int closingDay, int dueDay)
    {
        var previousMonth = new DateOnly(year, month, 1).AddMonths(-1);
        var start = ClosingDate(previousMonth.Year, previousMonth.Month, closingDay).AddDays(1);
        var end = ClosingDate(year, month, closingDay);
        return new CreditCardCycle(year, month, start, end, PaymentDueDate(year, month, closingDay, dueDay));
    }

    /// <summary>
    /// Ciclo em aberto a <paramref name="date"/>: o primeiro cujo fecho é ≥ à
    /// data, a começar no mês anterior (com fecho 31, o dia 1/03 ainda
    /// pertence ao ciclo de fevereiro, que fecha a 1/03).
    /// </summary>
    public static CreditCardCycle CycleContaining(DateOnly date, int closingDay, int dueDay)
    {
        var candidate = new DateOnly(date.Year, date.Month, 1).AddMonths(-1);
        while (ClosingDate(candidate.Year, candidate.Month, closingDay) < date)
        {
            candidate = candidate.AddMonths(1);
        }

        return Cycle(candidate.Year, candidate.Month, closingDay, dueDay);
    }

    public static CreditCardCycle Previous(CreditCardCycle cycle, int closingDay, int dueDay)
    {
        var previousMonth = new DateOnly(cycle.Year, cycle.Month, 1).AddMonths(-1);
        return Cycle(previousMonth.Year, previousMonth.Month, closingDay, dueDay);
    }
}
