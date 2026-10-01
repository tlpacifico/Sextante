using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Infrastructure.StatementConversion;

namespace Sextante.Modules.Financial.Application.Tests.StatementConverters;

public sealed class ActivoBankAccountXlsxConverterTests
{
    private readonly ActivoBankAccountXlsxConverter _converter = new(NullLogger<ActivoBankAccountXlsxConverter>.Instance);

    private static DateTime D(int month, int day) => new(2026, month, day);

    private static object?[] R(params object?[] cells) => cells;

    /// <summary>Movimentos sintéticos; o saldo encadeia a partir de 884,61.</summary>
    private static List<object?[]> Movements() => new()
    {
        R(D(9, 1), D(9, 1), "PAG BXVAL- 0000 VIAVERDE", -20.3, 864.31),
        R(D(9, 2), D(9, 2), "TRF. P/O  EMPRESA EXEMPLO LDA", 616, 1480.31),
        R(D(9, 3), D(9, 3), "COMPRA 0000 LIVRARIA EXEMPLO", -37.03, 1443.28),
        R(D(9, 7), D(9, 7), "TRF. P/ JOAO EXEMPLO", -90.96, 1352.32),
        R(D(9, 7), D(9, 7), "TRF. P/ JOAO EXEMPLO", -90.96, 1261.36),
        R(D(10, 1), D(10, 1), "DD GINASIO EXEMPLO 0000", -45, 1216.36),
    };

    private static byte[] Workbook(IEnumerable<object?[]> movements, bool withHeader = true)
    {
        var rows = new List<object?[]>
        {
            new object?[] { "HISTÓRICO DE CONTA NÚMERO 0000" },
            R("Moeda:", "EUR"),
            R("", ""),
            R("Tipo:", "Todos"),
            R("Data de:", D(9, 1)),
            R("Data até:", D(10, 1)),
            R((object?)null),
        };
        if (withHeader)
            rows.Add(R("Data Lanc.", "Data Valor", "Descrição", "Valor", "Saldo"));
        rows.AddRange(movements);
        return XlsxFixtureBuilder.Build(rows);
    }

    private StatementConversionResult Convert(byte[] xlsx) => _converter.Convert(new MemoryStream(xlsx), CancellationToken.None);

    [Fact]
    public void Converts_rows_and_chains_the_balance()
    {
        var result = Convert(Workbook(Movements()));

        result.Format.Should().Be(StatementFormat.ActivoBankAccountXlsx);
        result.Rows.Should().HaveCount(6);
        result.Rows[0].Should().Be(new StatementRow(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), "PAG BXVAL- 0000 VIAVERDE", -20.30m, 864.31m));
        result.Rows[1].SignedAmount.Should().Be(616m);
        result.BalanceBefore.Should().Be(884.61m);
        result.BalanceAfter.Should().Be(1216.36m);
        result.PeriodStart.Should().Be(new DateOnly(2026, 9, 1));
        result.PeriodEnd.Should().Be(new DateOnly(2026, 10, 1));
        result.Currency.Should().Be("EUR");
        result.HasRowBalances.Should().BeTrue();
        result.Checks.Should().ContainSingle().Which.Passed.Should().BeTrue();
        result.SourceAccountHint.Should().Contain("0000");
    }

    [Fact]
    public void Two_identical_movements_on_the_same_day_are_both_kept()
    {
        var result = Convert(Workbook(Movements()));

        result.Rows.Where(r => r.Description == "TRF. P/ JOAO EXEMPLO").Should().HaveCount(2);
    }

    [Fact]
    public void Descending_export_is_normalized_by_the_balance_chain()
    {
        var reversed = Movements();
        reversed.Reverse();

        var result = Convert(Workbook(reversed));

        result.Rows.Select(r => r.Balance).Should().Equal(864.31m, 1480.31m, 1443.28m, 1352.32m, 1261.36m, 1216.36m);
        result.BalanceBefore.Should().Be(884.61m);
    }

    [Fact]
    public void Tampered_balance_fails_with_line_expected_and_found()
    {
        var movements = Movements();
        movements[3][4] = 1352.99; // devia ser 1352.32

        var act = () => Convert(Workbook(movements));

        act.Should().Throw<StatementValidationException>()
            .WithMessage("*movimento 4*07/09/2026*esperado 1352,32*encontrado 1352,99*");
    }

    [Fact]
    public void Sheet_without_the_header_row_is_a_layout_error()
    {
        var act = () => Convert(Workbook(Movements(), withHeader: false));

        act.Should().Throw<StatementLayoutNotSupportedException>().WithMessage("*Data Lanc.*");
    }

    [Fact]
    public void Row_without_amount_fails_with_the_row_number()
    {
        var movements = Movements();
        movements[2][3] = null;

        var act = () => Convert(Workbook(movements));

        act.Should().Throw<StatementValidationException>().WithMessage("Linha 11*valor*");
    }

    [Fact]
    public void Row_without_a_date_fails_with_the_row_number()
    {
        var movements = Movements();
        movements[1][0] = "ontem";

        var act = () => Convert(Workbook(movements));

        act.Should().Throw<StatementValidationException>().WithMessage("Linha 10*data*");
    }

    [Fact]
    public void Empty_statement_is_rejected()
    {
        var act = () => Convert(Workbook([]));

        act.Should().Throw<StatementValidationException>().WithMessage("*não tem movimentos*");
    }

    [Fact]
    public void File_that_is_not_an_xlsx_is_unreadable()
    {
        var act = () => Convert("isto não é um xlsx"u8.ToArray());

        act.Should().Throw<StatementUnreadableException>();
    }

    [Theory]
    [InlineData("extrato.xlsx", true)]
    [InlineData("EXTRATO.XLSX", true)]
    [InlineData("extrato.csv", false)]
    [InlineData("extrato.pdf", false)]
    public void Handles_only_xlsx_files(string fileName, bool expected)
        => _converter.CanHandle(fileName).Should().Be(expected);

    [Fact]
    public void Converted_rows_adapt_to_the_canonical_csv()
    {
        var csv = CanonicalCsvAdapter.Adapt(Convert(Workbook(Movements())));

        csv.Rows[1].Should().Equal("02/09/2026", "02/09/2026", "TRF. P/O EMPRESA EXEMPLO LDA", "616,00", "1480,31");
    }
}
