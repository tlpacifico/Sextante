using FluentAssertions;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Application.Tests.TestSupport;
using Sextante.Modules.Financial.Domain.Common;

namespace Sextante.Modules.Financial.Application.Tests.StatementConverters;

public sealed class StatementOverlapTrimmerTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static StatementConversionResult Trim(FakeAccountLedger ledger, StatementConversionResult result)
        => new StatementOverlapTrimmer(ledger, ledger).TrimAsync(result, AccountId, CancellationToken.None).GetAwaiter().GetResult();

    private static StatementRow Row(DateOnly date, string description, decimal amount, decimal? balance)
        => new(date, date, description, amount, balance);

    // ---- Conta com saldo por linha (XLSX) --------------------------------------------------

    /// <summary>Extrato de 01/09 a 15/09 com 2 transferências idênticas a 07/09 (90,96 € ×2).</summary>
    private static StatementConversionResult AccountStatement() => new(
        StatementFormat.ActivoBankAccountXlsx,
        [
            Row(D(9, 1), "COMPRA A", -20.00m, 980.00m),
            Row(D(9, 7), "TRF. P/ JOAO", -90.96m, 889.04m),
            Row(D(9, 7), "TRF. P/ JOAO", -90.96m, 798.08m),
            Row(D(9, 15), "COMPRA B", -8.08m, 790.00m),
        ],
        1000.00m, 790.00m, D(9, 1), D(9, 15),
        [new StatementCheck("Saldo encadeado linha a linha", true, "790,00", "790,00")]);

    [Fact]
    public void Account_without_movements_has_nothing_to_trim()
    {
        var trimmed = Trim(new FakeAccountLedger(1000.00m), AccountStatement());

        trimmed.Rows.Should().HaveCount(4);
        trimmed.RowsTrimmed.Should().Be(0);
    }

    [Fact]
    public void Reimporting_the_same_statement_leaves_zero_rows()
    {
        var ledger = new FakeAccountLedger(1000.00m,
            (D(9, 1), -20.00m), (D(9, 7), -90.96m), (D(9, 7), -90.96m), (D(9, 15), -8.08m));

        var trimmed = Trim(ledger, AccountStatement());

        trimmed.Rows.Should().BeEmpty();
        trimmed.RowsTrimmed.Should().Be(4);
        trimmed.Checks.Should().Contain(c => c.Name.Contains("coincide"));
    }

    [Fact]
    public void Partially_overlapping_statement_keeps_only_the_new_rows()
    {
        var ledger = new FakeAccountLedger(1000.00m, (D(9, 1), -20.00m), (D(9, 7), -90.96m), (D(9, 7), -90.96m));

        var trimmed = Trim(ledger, AccountStatement());

        trimmed.Rows.Should().ContainSingle().Which.Description.Should().Be("COMPRA B");
        trimmed.RowsTrimmed.Should().Be(3);
        trimmed.BalanceBefore.Should().Be(798.08m);
    }

    [Fact]
    public void Two_identical_same_day_movements_are_not_mistaken_for_duplicates()
    {
        // Só a 1.ª das duas transferências idênticas está na conta: a 2.ª tem de ficar.
        var ledger = new FakeAccountLedger(1000.00m, (D(9, 1), -20.00m), (D(9, 7), -90.96m));

        var trimmed = Trim(ledger, AccountStatement());

        trimmed.Rows.Select(r => r.Description).Should().Equal("TRF. P/ JOAO", "COMPRA B");
        trimmed.Rows[0].Balance.Should().Be(798.08m);
        trimmed.RowsTrimmed.Should().Be(2);
    }

    [Fact]
    public void Statement_that_starts_after_the_last_movement_is_untouched()
    {
        var ledger = new FakeAccountLedger(1000.00m, (D(8, 30), -5.00m));

        var trimmed = Trim(ledger, AccountStatement());

        trimmed.Rows.Should().HaveCount(4);
        trimmed.RowsTrimmed.Should().Be(0);
    }

    [Fact]
    public void Balance_that_matches_no_statement_row_fails_with_an_explanation()
    {
        var ledger = new FakeAccountLedger(1000.00m, (D(9, 1), -20.00m), (D(9, 7), -91.00m));

        var act = () => Trim(ledger, AccountStatement());

        act.Should().Throw<StatementValidationException>()
            .WithMessage("*divergem*07/09/2026*889,00*nenhuma linha*");
    }

    [Fact]
    public void Coverflex_rows_anchor_on_the_balance_the_same_way()
    {
        var coverflex = new StatementConversionResult(
            StatementFormat.CoverflexJson,
            [
                Row(D(1, 6), "COVERFLEX TOPUP", 211.20m, 211.45m),
                Row(D(1, 7), "COMPRA X", -9.37m, 202.08m),
                Row(D(1, 8), "COMPRA Y", -2.00m, 200.08m),
            ],
            0.25m, 200.08m, D(1, 6), D(1, 8), []);
        var ledger = new FakeAccountLedger(0.25m, (D(1, 6), 211.20m), (D(1, 7), -9.37m));

        var trimmed = Trim(ledger, coverflex);

        trimmed.Rows.Should().ContainSingle().Which.Description.Should().Be("COMPRA Y");
    }

    // ---- Cartão (sem saldo por linha) -------------------------------------------------------

    /// <summary>Extrato de fevereiro com um movimento de 30/01 (data anterior ao período).</summary>
    private static StatementConversionResult CardStatement() => new(
        StatementFormat.ActivoBankCardPdf,
        [
            Row(D(1, 30), "COMPRA ANTES DO PERIODO", -4.99m, null),
            Row(D(2, 3), "COMPRA C", -10.00m, null),
            Row(D(2, 20), "PAGAMENTO", 100.00m, null),
        ],
        -300.00m, -214.99m, D(2, 1), D(2, 28),
        []);

    [Fact]
    public void Card_statement_after_the_last_movement_checks_the_previous_debt_and_keeps_every_row()
    {
        // Janeiro importado: dívida 300,00 e último movimento a 30/01 (= data do 1.º movimento de fevereiro).
        var ledger = new FakeAccountLedger(-250.00m, (D(1, 15), -50.00m), (D(1, 30), 0m));

        var trimmed = Trim(ledger, CardStatement());

        trimmed.Rows.Should().HaveCount(3);
        trimmed.RowsTrimmed.Should().Be(0);
        trimmed.Checks.Should().ContainSingle(c => c.Name.Contains("Dívida anterior") && c.Passed);
    }

    [Fact]
    public void Card_statement_whose_previous_debt_does_not_match_the_account_fails()
    {
        var ledger = new FakeAccountLedger(-250.00m, (D(1, 15), -40.00m), (D(1, 30), 0m));

        var act = () => Trim(ledger, CardStatement());

        act.Should().Throw<StatementValidationException>()
            .WithMessage("*dívida anterior*300,00*31/01/2026*290,00*");
    }

    [Fact]
    public void Card_statement_already_imported_is_trimmed_to_zero_rows_without_a_debt_check()
    {
        // O movimento de 30/01 já foi importado com este extrato (por isso o saldo a 31/01 ≠ dívida anterior).
        var ledger = new FakeAccountLedger(-300.00m, (D(1, 30), -4.99m), (D(2, 3), -10.00m), (D(2, 20), 100.00m));

        var trimmed = Trim(ledger, CardStatement());

        trimmed.Rows.Should().BeEmpty();
        trimmed.RowsTrimmed.Should().Be(3);
    }

    [Fact]
    public void Card_statement_partially_imported_keeps_only_rows_after_the_last_movement()
    {
        var ledger = new FakeAccountLedger(-300.00m, (D(1, 30), -4.99m), (D(2, 3), -10.00m));

        var trimmed = Trim(ledger, CardStatement());

        trimmed.Rows.Should().ContainSingle().Which.Description.Should().Be("PAGAMENTO");
        trimmed.RowsTrimmed.Should().Be(2);
    }

    [Fact]
    public void Card_account_without_movements_has_nothing_to_trim()
    {
        var trimmed = Trim(new FakeAccountLedger(-300.00m), CardStatement());

        trimmed.Rows.Should().HaveCount(3);
        trimmed.Checks.Should().BeEmpty();
    }
}
