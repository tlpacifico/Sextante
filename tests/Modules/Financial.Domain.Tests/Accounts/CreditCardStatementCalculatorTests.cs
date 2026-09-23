using System.Globalization;
using FluentAssertions;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.AccountsSpec;

/// <summary>
/// Limite 2 000, fecho 20, pagamento 10; hoje 2026-09-23 → ciclo corrente
/// 2026-09-21..2026-10-20, anterior 2026-08-21..2026-09-20 (paga a 2026-10-10).
/// </summary>
public sealed class CreditCardStatementCalculatorTests
{
    private static readonly DateOnly Today = D("2026-09-23");

    private static DateOnly D(string value) => DateOnly.Parse(value, CultureInfo.InvariantCulture);

    private static CreditCardSettings Settings()
    {
        var card = Account.Create("Cartão", AccountType.CreditCard, "EUR", new Money(0m, "EUR"), TenantId.New());
        card.ConfigureCreditCard(new Money(2000m, "EUR"), 20, 10, null);
        return card.CreditCard!;
    }

    private static CreditCardMovement M(string date, TransactionKind kind, TransactionDirection direction, decimal amount)
        => new(D(date), kind, direction, amount);

    private static CreditCardStatement Calc(
        decimal currentBalance = 0m, decimal balanceAtPreviousClose = 0m, params CreditCardMovement[] movements)
        => CalcWithInstallments(currentBalance, balanceAtPreviousClose, 0m, movements);

    private static CreditCardStatement CalcWithInstallments(
        decimal currentBalance, decimal balanceAtPreviousClose, decimal unbilled, params CreditCardMovement[] movements)
        => CreditCardStatementCalculator.Calculate(
            Settings(), Today, currentBalance, balanceAtPreviousClose, movements, unbilled);

    [Fact]
    public void Cycles_follow_the_calendar()
    {
        var statement = Calc();

        statement.CurrentCycle.Cycle.Start.Should().Be(D("2026-09-21"));
        statement.CurrentCycle.Cycle.End.Should().Be(D("2026-10-20"));
        statement.PreviousCycle.Cycle.Start.Should().Be(D("2026-08-21"));
        statement.PreviousCycle.Cycle.End.Should().Be(D("2026-09-20"));
        statement.NextPaymentDueDate.Should().Be(D("2026-10-10"));
    }

    [Fact]
    public void Spent_is_regular_outflow_minus_regular_inflow_in_cycle()
    {
        var statement = Calc(movements:
        [
            M("2026-09-22", TransactionKind.Regular, TransactionDirection.Outflow, 100m),
            M("2026-09-22", TransactionKind.Regular, TransactionDirection.Inflow, 30m),
            M("2026-09-19", TransactionKind.Regular, TransactionDirection.Outflow, 999m),
            M("2026-09-20", TransactionKind.Regular, TransactionDirection.Outflow, 1m),
            M("2026-08-20", TransactionKind.Regular, TransactionDirection.Outflow, 5000m),
        ]);

        statement.CurrentCycle.Spent.Should().Be(70m);
        statement.PreviousCycle.Spent.Should().Be(1000m);
    }

    [Fact]
    public void Transfer_inflow_is_a_payment_not_a_spend()
    {
        var statement = Calc(movements:
        [
            M("2026-09-22", TransactionKind.Transfer, TransactionDirection.Inflow, 400m),
        ]);

        statement.CurrentCycle.PaymentsReceived.Should().Be(400m);
        statement.CurrentCycle.Spent.Should().Be(0m);
    }

    [Fact]
    public void Adjustments_and_outgoing_transfers_are_neither_spend_nor_payment()
    {
        var statement = Calc(movements:
        [
            M("2026-09-22", TransactionKind.Adjustment, TransactionDirection.Outflow, 50m),
            M("2026-09-22", TransactionKind.Transfer, TransactionDirection.Outflow, 20m),
        ]);

        statement.CurrentCycle.Spent.Should().Be(0m);
        statement.CurrentCycle.PaymentsReceived.Should().Be(0m);
    }

    [Fact]
    public void Debt_available_and_next_payment()
    {
        var statement = Calc(-1200m, -1500m,
            M("2026-09-22", TransactionKind.Transfer, TransactionDirection.Inflow, 400m));

        statement.CurrentDebt.Should().Be(1200m);
        statement.Available.Should().Be(800m);
        statement.PreviousClosingDebt.Should().Be(1500m);
        statement.NextPaymentAmount.Should().Be(1100m);
        statement.NextPaymentDueDate.Should().Be(D("2026-10-10"));
    }

    [Fact]
    public void Next_payment_never_negative()
    {
        var statement = Calc(200m, -300m,
            M("2026-09-22", TransactionKind.Transfer, TransactionDirection.Inflow, 500m));

        statement.NextPaymentAmount.Should().Be(0m);
    }

    [Fact]
    public void Next_payment_discounts_unbilled_installments()
    {
        // Grupo 6 — as prestações futuras estão na dívida do fecho mas ainda
        // não se pagam: 1 000 − 400 por faturar − 100 já pagos = 500.
        var statement = CalcWithInstallments(-900m, -1000m, 400m,
            M("2026-09-22", TransactionKind.Transfer, TransactionDirection.Inflow, 100m));

        statement.UnbilledInstallmentsAtPreviousClose.Should().Be(400m);
        statement.NextPaymentAmount.Should().Be(500m);
        statement.PreviousClosingDebt.Should().Be(1000m);
    }

    [Fact]
    public void Next_payment_with_installments_never_negative()
    {
        CalcWithInstallments(-300m, -300m, 400m).NextPaymentAmount.Should().Be(0m);
    }

    [Fact]
    public void Positive_balance_means_no_debt()
    {
        var statement = Calc(50m, 20m);

        statement.CurrentDebt.Should().Be(0m);
        statement.PreviousClosingDebt.Should().Be(0m);
        statement.Available.Should().Be(2050m);
    }

    [Fact]
    public void Over_limit_available_is_negative()
    {
        Calc(-2100m).Available.Should().Be(-100m);
    }
}
