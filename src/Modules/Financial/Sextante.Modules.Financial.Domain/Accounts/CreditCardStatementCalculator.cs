using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Domain.Accounts;

/// <summary>
/// Movimento de um cartão reduzido ao necessário para o extrato: data (UTC),
/// tipo, direção e valor (sempre &gt; 0).
/// </summary>
public sealed record CreditCardMovement(DateOnly Date, TransactionKind Kind, TransactionDirection Direction, decimal Amount);

/// <summary>
/// <see cref="Spent"/> = despesas regulares menos reembolsos regulares;
/// <see cref="PaymentsReceived"/> = transferências recebidas (pagamentos do
/// cartão). Acertos não entram em nenhum dos dois.
/// </summary>
public sealed record CreditCardCycleTotals(CreditCardCycle Cycle, decimal Spent, decimal PaymentsReceived);

public sealed record CreditCardStatement(
    CreditCardCycleTotals CurrentCycle,
    CreditCardCycleTotals PreviousCycle,
    decimal PreviousClosingDebt,
    decimal CurrentDebt,
    decimal Available,
    DateOnly NextPaymentDueDate,
    decimal NextPaymentAmount);

/// <summary>
/// Extrato calculado de um cartão (Phase 6.5 grupo 5), sem nada persistido:
/// dívida = −saldo (nunca negativa), disponível = limite + saldo (negativo
/// se passou o limite), próximo pagamento = dívida no último fecho menos os
/// pagamentos recebidos desde esse fecho (nunca negativo). As prestações
/// (grupo 6) ainda não entram.
/// </summary>
public static class CreditCardStatementCalculator
{
    public static CreditCardStatement Calculate(
        CreditCardSettings settings,
        DateOnly today,
        decimal currentBalance,
        decimal balanceAtPreviousClose,
        IReadOnlyCollection<CreditCardMovement> movements)
    {
        var closingDay = settings.StatementClosingDay;
        var dueDay = settings.PaymentDueDay;

        var current = Totals(CreditCardCalendar.CycleContaining(today, closingDay, dueDay), movements);
        var previous = Totals(CreditCardCalendar.Previous(current.Cycle, closingDay, dueDay), movements);

        var previousClosingDebt = Debt(balanceAtPreviousClose);

        return new CreditCardStatement(
            current,
            previous,
            previousClosingDebt,
            Debt(currentBalance),
            settings.CreditLimitAmount + currentBalance,
            previous.Cycle.PaymentDueDate,
            Math.Max(0m, previousClosingDebt - current.PaymentsReceived));
    }

    private static decimal Debt(decimal balance) => Math.Max(0m, -balance);

    private static CreditCardCycleTotals Totals(CreditCardCycle cycle, IReadOnlyCollection<CreditCardMovement> movements)
    {
        var spent = 0m;
        var payments = 0m;

        foreach (var movement in movements)
        {
            if (movement.Date < cycle.Start || movement.Date > cycle.End)
            {
                continue;
            }

            switch (movement.Kind, movement.Direction)
            {
                case (TransactionKind.Regular, TransactionDirection.Outflow):
                    spent += movement.Amount;
                    break;
                case (TransactionKind.Regular, TransactionDirection.Inflow):
                    spent -= movement.Amount;
                    break;
                case (TransactionKind.Transfer, TransactionDirection.Inflow):
                    payments += movement.Amount;
                    break;
            }
        }

        return new CreditCardCycleTotals(cycle, spent, payments);
    }
}
