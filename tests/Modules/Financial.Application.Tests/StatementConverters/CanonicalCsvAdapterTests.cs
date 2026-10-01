using FluentAssertions;
using Sextante.Modules.Financial.Application.Features.CsvImport;
using Sextante.Modules.Financial.Application.StatementConversion;

namespace Sextante.Modules.Financial.Application.Tests.StatementConverters;

/// <summary>
/// Phase 6.6 (0.2) — o formato canónico do adaptador tem de ser o das
/// fixtures CSV sintéticas do import (D2).
/// </summary>
public sealed class CanonicalCsvAdapterTests
{
    private static StatementRow Row(string date, string description, decimal amount, decimal? balance)
    {
        var d = DateOnly.ParseExact(date, "yyyy-MM-dd");
        return new StatementRow(d, d, description, amount, balance);
    }

    private static StatementConversionResult Result(params StatementRow[] rows)
        => new(StatementFormat.ActivoBankAccountXlsx, rows, null, null, null, null, []);

    [Fact]
    public void Account_rows_produce_the_synthetic_account_csv()
    {
        var result = Result(
            Row("2026-08-27", "COMPRA PADARIA EXEMPLO", -3.20m, 1000.00m),
            Row("2026-09-02", "TRANSFERENCIA - VENCIMENTO", 1500.00m, 2500.00m),
            Row("2026-09-11", "VIS PAGAMENTO CARTAO DE CREDITO", -450.00m, 2050.00m),
            Row("2026-09-15", "COMPRA SUPERMERCADO EXEMPLO", -62.35m, 1987.65m));

        var csv = CanonicalCsvAdapter.Adapt(result);

        csv.Headers.Should().Equal("Data Lanc.", "Data Valor", "Descrição", "Valor", "Saldo");
        var expected = ReadFixture("activo-conta-sintetico.csv");
        csv.Rows.Select(Join).Should().Equal(expected.Take(4));
    }

    [Fact]
    public void Card_rows_without_balance_leave_the_balance_column_empty()
    {
        var result = Result(
            Row("2026-08-20", "COMPRA LOJA EXEMPLO", -120.00m, null),
            Row("2026-09-14", "PAGAMENTO RECEBIDO OBRIGADO", 450.00m, null));

        var csv = CanonicalCsvAdapter.Adapt(result);

        csv.Rows.Select(Join).Should().Equal(
            "20/08/2026;20/08/2026;COMPRA LOJA EXEMPLO;-120,00;",
            "14/09/2026;14/09/2026;PAGAMENTO RECEBIDO OBRIGADO;450,00;");
    }

    [Fact]
    public void Settings_make_the_import_row_parser_read_the_rows_back()
    {
        var result = Result(Row("2026-09-02", "TRF. P/O  EMPRESA; \"X\"", 1234.50m, 2000m));

        var csv = CanonicalCsvAdapter.Adapt(result);
        var resolver = ImportRowParser.ResolverFor(csv.Headers, csv.Settings, profile: null);
        var parsed = ImportRowParser.Parse(csv.Rows[0], resolver, csv.Settings, out var error);

        error.Should().BeNull();
        parsed!.Date.Should().Be(new DateOnly(2026, 9, 2));
        parsed.SignedAmount.Should().Be(1234.50m);
        parsed.Description.Should().Be("TRF. P/O EMPRESA, 'X'");
    }

    [Fact]
    public void Rows_with_the_same_values_parse_like_the_fixture()
    {
        // A fixture escreve milhares com espaço ("1 000,00"); o adaptador não.
        // Ambos têm de dar o mesmo número ao parser do import.
        var result = Result(Row("2026-08-27", "COMPRA PADARIA EXEMPLO", -3.20m, 1000.00m));
        var csv = CanonicalCsvAdapter.Adapt(result);
        var fixtureLine = ReadFixture("activo-conta-sintetico.csv")[0].Split(';');
        var resolver = ImportRowParser.ResolverFor(csv.Headers, csv.Settings, profile: null);

        var ours = ImportRowParser.Parse(csv.Rows[0], resolver, csv.Settings, out _);
        var theirs = ImportRowParser.Parse(fixtureLine, resolver, csv.Settings, out _);

        ours.Should().BeEquivalentTo(theirs);
    }

    private static string Join(IReadOnlyList<string> row) => string.Join(';', row);

    private static List<string> ReadFixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "Sextante.IntegrationTests")))
            dir = dir.Parent;
        dir.Should().NotBeNull("a raiz do repositório tem de ser encontrada");

        var path = Path.Combine(dir!.FullName, "tests", "Sextante.IntegrationTests", "Data", "Import", name);
        // Normaliza o separador de milhares da fixture (espaço) para comparar conteúdo.
        return File.ReadAllLines(path).Skip(1)
            .Select(l =>
            {
                var cols = l.Split(';');
                cols[3] = cols[3].Replace(" ", "");
                cols[4] = cols[4].Replace(" ", "");
                return string.Join(';', cols);
            })
            .ToList();
    }
}
