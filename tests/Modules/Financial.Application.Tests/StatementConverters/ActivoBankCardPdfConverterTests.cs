using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Infrastructure.StatementConversion;
using Sextante.Modules.Financial.Infrastructure.StatementConversion.Pdf;

namespace Sextante.Modules.Financial.Application.Tests.StatementConverters;

public sealed class ActivoBankCardPdfConverterTests
{
    private readonly ActivoBankCardPdfConverter _converter = new(NullLogger<ActivoBankCardPdfConverter>.Instance);

    private StatementConversionResult Convert(byte[] pdf) => _converter.Convert(new MemoryStream(pdf), CancellationToken.None);

    // ---- Layout A (até agosto/2026) -------------------------------------------------------

    private static FixtureMovement[] LayoutAMovements() =>
    [
        new("2026/07/31", "2026/08/01", "COMPRA 0000 PADARIA EXEMPLO", 2.00m),
        new("2026/08/03", "2026/08/04", "COMPRA 0000 CONTINENTE BOM DIA 1150 CONT", 34.20m),
        new("2026/08/05", "2026/08/06", "COMPRA 0000 LOJA EXEMPLO USD TC 0.865589", 17.07m),
        new("2026/08/12", "2026/08/13", "COMPRA 0000 PORTATIL EXEMPLO", 1439.00m),
        new("2026/08/12", "2026/08/12", ">PAGAMENTO CARTAO DE CREDITO", 500.00m, IsCredit: true),
        new("2026/08/20", "2026/08/21", "DEVOLUCAO LOJA EXEMPLO", 12.50m, IsCredit: true),
    ];

    // débitos 1492,27 · créditos 512,50 · dívida anterior 1000,00 → atual 1979,77
    private static FixtureSummary LayoutASummary() => new(1000.00m, 512.50m, 1492.27m, 1979.77m);

    [Fact]
    public void Layout_A_converts_and_validates_the_summary()
    {
        var pdf = CardPdfFixtureBuilder.LayoutA(LayoutAMovements(), LayoutASummary()).ToPdf();

        var result = Convert(pdf);

        result.Format.Should().Be(StatementFormat.ActivoBankCardPdf);
        result.Rows.Should().HaveCount(6);
        result.Rows[0].Should().Be(new StatementRow(
            new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 1), "COMPRA 0000 PADARIA EXEMPLO", -2.00m, null));
        result.Rows[1].Description.Should().Be("COMPRA 0000 CONTINENTE BOM DIA 1150 CONT");
        result.Rows[2].Description.Should().Be("COMPRA 0000 LOJA EXEMPLO USD TC 0.865589");
        result.Rows[3].SignedAmount.Should().Be(-1439.00m); // milhares com espaço
        result.Rows[4].SignedAmount.Should().Be(500.00m);
        result.Rows[4].Description.Should().Be(">PAGAMENTO CARTAO DE CREDITO");
        result.Rows[5].SignedAmount.Should().Be(12.50m);
        result.BalanceBefore.Should().Be(-1000.00m);
        result.BalanceAfter.Should().Be(-1979.77m);
        result.PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
        result.PeriodEnd.Should().Be(new DateOnly(2026, 8, 31));
        result.Currency.Should().Be("EUR");
        result.HasRowBalances.Should().BeFalse();
        result.SourceAccountHint.Should().Be("000000-0000000-00-0");
        result.Checks.Should().HaveCount(3).And.OnlyContain(c => c.Passed);
    }

    [Fact]
    public void Layout_A_ignores_the_installment_table_and_the_network_line()
    {
        var result = Convert(CardPdfFixtureBuilder.LayoutA(LayoutAMovements(), LayoutASummary()).ToPdf());

        result.Rows.Should().NotContain(r => r.Description.Contains("PRESTACOES") || r.Description == "VIS");
    }

    [Fact]
    public void Layout_A_totals_that_do_not_match_the_summary_fail_with_expected_and_computed()
    {
        var summary = LayoutASummary() with { Debits = 1500.00m, CurrentDebt = 1987.50m };
        var pdf = CardPdfFixtureBuilder.LayoutA(LayoutAMovements(), summary).ToPdf();

        var act = () => Convert(pdf);

        act.Should().Throw<StatementValidationException>()
            .WithMessage("*Total de débitos*esperado 1500,00, calculado 1492,27*");
    }

    [Fact]
    public void Layout_A_debt_that_does_not_chain_fails_even_when_the_totals_match()
    {
        var summary = LayoutASummary() with { CurrentDebt = 1979.00m };
        var pdf = CardPdfFixtureBuilder.LayoutA(LayoutAMovements(), summary).ToPdf();

        var act = () => Convert(pdf);

        act.Should().Throw<StatementValidationException>()
            .WithMessage("*Dívida anterior + débitos − créditos*esperado 1979,00, calculado 1979,77*")
            .Where(e => !e.Message.Contains("Total de débitos") && !e.Message.Contains("Total de créditos"));
    }

    [Fact]
    public void A_credit_is_recognised_by_its_column_not_by_its_description()
    {
        var movements = new FixtureMovement[]
        {
            new("2026/08/03", "2026/08/04", "COMPRA 0000 LOJA EXEMPLO", 10.00m),
            new("2026/08/04", "2026/08/05", "ESTORNO QUALQUER COISA", 4.00m),          // débito, apesar do nome
            new("2026/08/05", "2026/08/06", "OPERACAO SEM PALAVRA CHAVE", 7.00m, IsCredit: true),
        };
        var pdf = CardPdfFixtureBuilder.LayoutA(movements, new FixtureSummary(100m, 7m, 14m, 107m)).ToPdf();

        var result = Convert(pdf);

        result.Rows.Select(r => r.SignedAmount).Should().Equal(-10m, -4m, 7m);
    }

    // ---- Layout B (desde setembro/2026) ----------------------------------------------------

    private static FixtureMovement[] LayoutBMovements() =>
    [
        new("09/08", "09/09", "COMPRA 0000 SERVICO EXEMPLO", 22.14m, Continuation: ["SUB DUBLIN"]),
        new("09/12", "09/14", "COMPRA 0000 SUPERMERCADO EXEMPLO", 13.16m, Network: "MB", Continuation: ["DIA ALCA CONT"]),
        new("09/14", "09/14", "COMPRA 0000 LOJA GRANDE EXEMPLO", 1439.00m),
        new("09/01", "09/01", ">PAGAMENTO CARTAO DE CREDITO", 668.66m, IsCredit: true),
    ];

    // débitos 1474,30 · créditos 668,66 · dívida anterior 2409,17 → atual 3214,81
    private static FixtureSummary LayoutBSummary() => new(2409.17m, 668.66m, 1474.30m, 3214.81m);

    [Fact]
    public void Layout_B_converts_with_multiline_descriptions_and_without_the_network()
    {
        var pdf = CardPdfFixtureBuilder.LayoutB(LayoutBMovements(), LayoutBSummary()).ToPdf();

        var result = Convert(pdf);

        result.Rows.Should().HaveCount(4);
        result.Rows[0].Should().Be(new StatementRow(
            new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 9), "COMPRA 0000 SERVICO EXEMPLO SUB DUBLIN", -22.14m, null));
        result.Rows[1].Description.Should().Be("COMPRA 0000 SUPERMERCADO EXEMPLO DIA ALCA CONT");
        result.Rows[2].SignedAmount.Should().Be(-1439.00m);
        result.Rows[3].Description.Should().Be("PAGAMENTO CARTAO DE CREDITO"); // B tira o ">"
        result.Rows[3].SignedAmount.Should().Be(668.66m);
        result.BalanceBefore.Should().Be(-2409.17m);
        result.BalanceAfter.Should().Be(-3214.81m);
        result.Checks.Should().HaveCount(3).And.OnlyContain(c => c.Passed);
    }

    [Fact]
    public void Layout_B_footer_lines_are_not_glued_to_the_last_description()
    {
        var result = Convert(CardPdfFixtureBuilder.LayoutB(LayoutBMovements(), LayoutBSummary()).ToPdf());

        result.Rows[^1].Description.Should().NotContain("Pág").And.NotContain("351");
    }

    [Fact]
    public void Layout_B_deduces_the_year_from_the_period_across_a_year_boundary()
    {
        var movements = new FixtureMovement[]
        {
            new("12/31", "01/02", "COMPRA 0000 LOJA EXEMPLO", 10.00m),
            new("01/05", "01/06", "COMPRA 0000 OUTRA LOJA EXEMPLO", 20.00m),
        };
        var pdf = CardPdfFixtureBuilder.LayoutB(movements, new FixtureSummary(0m, 0m, 30m, 30m), "2027/01/01", "2027/01/31").ToPdf();

        var result = Convert(pdf);

        result.Rows[0].Date.Should().Be(new DateOnly(2026, 12, 31));
        result.Rows[0].ValueDate.Should().Be(new DateOnly(2027, 1, 2));
        result.Rows[1].Date.Should().Be(new DateOnly(2027, 1, 5));
        result.Rows[1].ValueDate.Should().Be(new DateOnly(2027, 1, 6));
    }

    [Fact]
    public void Layout_B_totals_that_do_not_match_fail()
    {
        var summary = LayoutBSummary() with { Credits = 700.00m };
        var pdf = CardPdfFixtureBuilder.LayoutB(LayoutBMovements(), summary).ToPdf();

        var act = () => Convert(pdf);

        act.Should().Throw<StatementValidationException>().WithMessage("*Total de créditos*esperado 700,00, calculado 668,66*");
    }

    // ---- Parsers sobre o modelo (sem PDF) ---------------------------------------------------

    [Fact]
    public void Layout_detector_tells_the_two_layouts_apart()
    {
        CardLayoutDetector.Detect(CardPdfFixtureBuilder.LayoutA(LayoutAMovements(), LayoutASummary()).ToPages())
            .Should().Be(CardLayout.A);
        CardLayoutDetector.Detect(CardPdfFixtureBuilder.LayoutB(LayoutBMovements(), LayoutBSummary()).ToPages())
            .Should().Be(CardLayout.B);
    }

    [Fact]
    public void Layout_detector_uses_the_header_when_there_are_no_movements()
    {
        CardLayoutDetector.Detect(CardPdfFixtureBuilder.LayoutA([], LayoutASummary()).ToPages()).Should().Be(CardLayout.A);
        CardLayoutDetector.Detect(CardPdfFixtureBuilder.LayoutB([], LayoutBSummary()).ToPages()).Should().Be(CardLayout.B);
    }

    [Fact]
    public void Parser_A_reads_rows_straight_from_the_page_model()
    {
        var pages = CardPdfFixtureBuilder.LayoutA(LayoutAMovements(), LayoutASummary()).ToPages();

        var rows = CardLayoutAParser.Parse(pages);

        rows.Should().HaveCount(6);
        rows.Count(r => r.IsCredit).Should().Be(2);
        rows.Where(r => !r.IsCredit).Sum(r => r.Amount).Should().Be(1492.27m);
    }

    [Fact]
    public void Header_parser_reads_period_currency_card_and_summary()
    {
        var pages = CardPdfFixtureBuilder.LayoutB(LayoutBMovements(), LayoutBSummary()).ToPages();

        var header = CardStatementHeaderParser.Parse(pages);

        header.PeriodStart.Should().Be(new DateOnly(2026, 9, 1));
        header.PeriodEnd.Should().Be(new DateOnly(2026, 9, 30));
        header.Currency.Should().Be("EUR");
        header.Summary.Should().Be(new CardSummary(2409.17m, 668.66m, 1474.30m, 3214.81m));
    }

    // ---- Falhas ---------------------------------------------------------------------------

    [Fact]
    public void Unknown_movements_layout_is_refused_instead_of_guessed()
    {
        var pdf = CardPdfFixtureBuilder.UnknownLayout(LayoutASummary()).ToPdf();

        var act = () => Convert(pdf);

        act.Should().Throw<StatementLayoutNotSupportedException>().WithMessage("*Layout de extrato não suportado*");
    }

    [Fact]
    public void Pdf_without_the_movements_summary_is_refused()
    {
        var act = () => Convert(CardPdfFixtureBuilder.WithoutSummary().ToPdf());

        act.Should().Throw<StatementLayoutNotSupportedException>().WithMessage("*RESUMO DE MOVIMENTOS*");
    }

    [Fact]
    public void Statement_without_movements_is_rejected()
    {
        var pdf = CardPdfFixtureBuilder.LayoutA([], new FixtureSummary(100m, 0m, 0m, 100m)).ToPdf();

        var act = () => Convert(pdf);

        act.Should().Throw<StatementValidationException>().WithMessage("*não tem movimentos*");
    }

    [Fact]
    public void Corrupted_pdf_is_an_explicit_error_not_a_crash()
    {
        var act = () => Convert("%PDF-1.7 isto não é um pdf de verdade"u8.ToArray());

        act.Should().Throw<StatementUnreadableException>().WithMessage("*PDF legível*");
    }

    [Theory]
    [InlineData("extrato.pdf", true)]
    [InlineData("EXTRATO.PDF", true)]
    [InlineData("extrato.xlsx", false)]
    [InlineData("extrato.json", false)]
    public void Handles_only_pdf_files(string fileName, bool expected)
        => _converter.CanHandle(fileName).Should().Be(expected);
}
