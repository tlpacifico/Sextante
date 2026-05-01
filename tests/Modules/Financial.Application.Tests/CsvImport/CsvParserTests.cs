using System.Text;
using FluentAssertions;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Infrastructure.CsvImport;

namespace Sextante.Modules.Financial.Application.Tests.CsvImport;

public sealed class CsvParserTests
{
    private static readonly CsvParser Parser = new();

    static CsvParserTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static Stream Stream(string content, Encoding? encoding = null)
        => new MemoryStream((encoding ?? Encoding.UTF8).GetBytes(content));

    [Fact]
    public void Parses_csv_with_header_and_semicolon_delimiter()
    {
        var csv = "Data;Valor;Descrição\n01-04-2026;10,50;café\n02-04-2026;20,00;almoço\n";
        var result = Parser.Parse(Stream(csv), new CsvParseOptions(), CancellationToken.None);

        result.Headers.Should().ContainInOrder("Data", "Valor", "Descrição");
        result.Rows.Should().HaveCount(2);
        result.Rows[0].Should().ContainInOrder("01-04-2026", "10,50", "café");
        result.TotalRowCount.Should().Be(2);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void Auto_detects_comma_delimiter()
    {
        var csv = "Date,Amount,Description\n2026-04-01,10.50,coffee\n2026-04-02,20.00,lunch\n";
        var result = Parser.Parse(Stream(csv), new CsvParseOptions(), CancellationToken.None);

        result.Headers.Should().ContainInOrder("Date", "Amount", "Description");
        result.Rows[0][1].Should().Be("10.50");
    }

    [Fact]
    public void Auto_detects_tab_delimiter()
    {
        var csv = "Data\tValor\tDescrição\n01-04-2026\t10,50\tcafé\n";
        var result = Parser.Parse(Stream(csv), new CsvParseOptions(), CancellationToken.None);

        result.Headers.Should().ContainInOrder("Data", "Valor", "Descrição");
        result.Rows[0].Should().ContainInOrder("01-04-2026", "10,50", "café");
    }

    [Fact]
    public void Honours_explicit_delimiter()
    {
        var csv = "Data|Valor\n01-04-2026|10,50\n";
        var result = Parser.Parse(Stream(csv), new CsvParseOptions(Delimiter: "|"), CancellationToken.None);

        result.Headers.Should().ContainInOrder("Data", "Valor");
        result.Rows[0].Should().ContainInOrder("01-04-2026", "10,50");
    }

    [Fact]
    public void Falls_back_to_iso_8859_1_when_utf8_invalid()
    {
        // ISO-8859-1 byte for 'ç' is 0xE7; not a valid 1-byte UTF-8 sequence on its own.
        var iso = Encoding.GetEncoding("ISO-8859-1");
        var csv = "Descrição\ncafé com bolo\n";
        var bytes = iso.GetBytes(csv);

        var result = Parser.Parse(new MemoryStream(bytes), new CsvParseOptions(), CancellationToken.None);

        result.Headers[0].Should().Be("Descrição");
        result.Rows[0][0].Should().Be("café com bolo");
    }

    [Fact]
    public void Supports_quoted_fields_with_delimiter_inside()
    {
        var csv = "Data;Descrição\n01-04-2026;\"campo com ; ponto-vírgula\"\n";
        var result = Parser.Parse(Stream(csv), new CsvParseOptions(), CancellationToken.None);

        result.Rows[0][1].Should().Be("campo com ; ponto-vírgula");
    }

    [Fact]
    public void Throws_when_csv_has_only_header()
    {
        var act = () => Parser.Parse(Stream("Data;Valor\n"), new CsvParseOptions(), CancellationToken.None);
        act.Should().Throw<CsvEmptyException>();
    }

    [Fact]
    public void Throws_when_csv_stream_is_truly_empty()
    {
        // Zero-byte stream has no header row at all → both UTF-8 and ISO-8859-1
        // passes find no header, so the parser surfaces EncodingNotSupported
        // (the deeper signal — the file is unparseable).
        var act = () => Parser.Parse(Stream(""), new CsvParseOptions(), CancellationToken.None);
        act.Should().Throw<FinancialDomainException>();
    }

    [Fact]
    public void Throws_on_duplicate_column_names()
    {
        var csv = "Data;Valor;Valor\n01-04-2026;10,50;10,50\n";
        var act = () => Parser.Parse(Stream(csv), new CsvParseOptions(Delimiter: ";"), CancellationToken.None);
        act.Should().Throw<CsvDuplicateColumnsException>();
    }

    [Fact]
    public void Truncates_preview_at_max_preview_rows()
    {
        var sb = new StringBuilder("Data;Valor\n");
        for (int i = 1; i <= 2500; i++)
            sb.Append($"01-04-2026;{i}\n");

        var result = Parser.Parse(Stream(sb.ToString()), new CsvParseOptions(MaxPreviewRows: 1000), CancellationToken.None);

        result.TotalRowCount.Should().Be(2500);
        result.Truncated.Should().BeTrue();
        result.Rows.Should().HaveCount(1000);
    }

    [Fact]
    public void Skips_leading_rows_when_skipRows_set()
    {
        var csv = "Data;Valor\nIGNORE;ME\nIGNORE;ME2\n01-04-2026;10\n";
        var result = Parser.Parse(Stream(csv), new CsvParseOptions(SkipRows: 2), CancellationToken.None);

        result.Rows.Should().HaveCount(1);
        result.Rows[0].Should().ContainInOrder("01-04-2026", "10");
    }

    [Fact]
    public void Honours_caller_cancellation_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sb = new StringBuilder("Data;Valor\n");
        for (int i = 0; i < 5000; i++) sb.Append($"01-04-2026;{i}\n");

        var act = () => Parser.Parse(Stream(sb.ToString()), new CsvParseOptions(), cts.Token);
        act.Should().Throw<OperationCanceledException>();
    }
}
