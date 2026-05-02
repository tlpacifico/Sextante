using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Budgets;

/// <summary>
/// Função pura: dado um <see cref="Budget"/>, contributions já
/// convertidas para a moeda do limit, e a data corrente, devolve
/// <see cref="BudgetProgress"/>. Sem DI, sem clock, sem ECB —
/// determinístico e testável.
/// </summary>
public static class BudgetProgressCalculator
{
    /// <summary>
    /// Mínimo de dias decorridos para começar a projetar fim de mês.
    /// Antes disso, projection = null (tendência ainda não confiável).
    /// </summary>
    public const int MinDaysForProjection = 5;

    public static BudgetProgress Calculate(
        Budget budget,
        IReadOnlyCollection<TransactionContribution> contributions,
        DateOnly today)
    {
        var currency = budget.Limit.Currency;

        var spentAmount = 0m;
        var hasIncomplete = false;
        foreach (var c in contributions)
        {
            if (c.RateMissing)
            {
                hasIncomplete = true;
                continue;
            }

            spentAmount += c.AmountInBudgetCurrency;
        }

        var spent = new Money(spentAmount, currency);
        var remaining = new Money(budget.Limit.Amount - spentAmount, currency);

        var percent = budget.Limit.Amount == 0m
            ? 0m
            : (spentAmount / budget.Limit.Amount) * 100m;

        Money? projected = null;
        if (budget.Period.Contains(today))
        {
            var daysElapsed = (today.DayNumber - budget.Period.Start.DayNumber) + 1;
            if (daysElapsed >= MinDaysForProjection && daysElapsed > 0)
            {
                var projectedAmount = (spentAmount / daysElapsed) * budget.Period.DaysInMonth;
                projected = new Money(projectedAmount, currency);
            }
        }
        else if (today > budget.Period.End)
        {
            // Período já terminou — não há projeção, spent é final.
            projected = null;
        }

        return new BudgetProgress(
            Limit: budget.Limit,
            Spent: spent,
            Remaining: remaining,
            PercentUsed: percent,
            ProjectedEndOfPeriod: projected,
            HasIncompleteRates: hasIncomplete);
    }
}

/// <summary>
/// Uma transaction já convertida (ou marcada como rate-missing)
/// para a moeda do Budget. Construída pela camada Application
/// a partir da query agregadora; o Calculator não conhece ECB.
/// </summary>
public sealed record TransactionContribution(
    decimal AmountInBudgetCurrency,
    bool RateMissing,
    DateOnly OccurredAt);

/// <summary>
/// Resultado do cálculo de progresso de um Budget.
/// <c>HasIncompleteRates</c> sinaliza ao UI que pelo menos uma
/// transaction foi ignorada por falta de taxa de câmbio (badge
/// "Cálculo parcial"). Phase 5b §requirements §Decisions:
/// degradação graciosa em ECB miss.
/// </summary>
public sealed record BudgetProgress(
    Money Limit,
    Money Spent,
    Money Remaining,
    decimal PercentUsed,
    Money? ProjectedEndOfPeriod,
    bool HasIncompleteRates);
