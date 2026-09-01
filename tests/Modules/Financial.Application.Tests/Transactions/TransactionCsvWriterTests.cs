using FluentAssertions;
using Sextante.Modules.Financial.Application.Features.Transactions;

namespace Sextante.Modules.Financial.Application.Tests.Transactions;

/// <summary>
/// Phase 6 (grupo 1.3) — o CSV de export tem de abrir directamente no
/// Excel em locale PT-PT: separador <c>;</c> e decimal <c>,</c>, o mesmo
/// formato que o wizard de import já consome.
/// </summary>
public sealed class TransactionCsvWriterTests
{
    private static readonly DateTimeOffset Occurred =
        new(2026, 8, 14, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Writes_header_and_row_in_pt_pt_format()
    {
        var csv = TransactionCsvWriter.Write(
        [
            Row(amount: 1234.5m, currency: "EUR", rate: null, description: "Supermercado"),
        ]);

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[0].Should().Be(
            "Data;Conta;Categoria;Tipo;Descrição;Valor;Moeda;Câmbio;ValorConvertido;MoedaPrincipal;Origem");
        lines[1].Should().Be(
            "2026-08-14;Conta Corrente;Alimentação;Despesa;Supermercado;1234,50;EUR;;1234,50;EUR;Manual");
    }

    [Fact]
    public void Quotes_description_containing_separator_or_quotes()
    {
        var csv = TransactionCsvWriter.Write(
        [
            Row(amount: 10m, currency: "EUR", rate: null, description: "Café; \"o bom\""),
        ]);

        var line = csv.Split("\r\n")[1];

        line.Should().Contain("\"Café; \"\"o bom\"\"\"");
    }

    [Fact]
    public void Converts_amount_using_stored_exchange_rate()
    {
        var csv = TransactionCsvWriter.Write(
        [
            Row(amount: 100m, currency: "USD", rate: 0.92m, description: null),
        ]);

        var fields = csv.Split("\r\n")[1].Split(';');

        fields[5].Should().Be("100,00");
        fields[6].Should().Be("USD");
        fields[7].Should().Be("0,92");
        fields[8].Should().Be("92,00");
    }

    private static TransactionExportRow Row(
        decimal amount,
        string currency,
        decimal? rate,
        string? description)
        => new(
            Occurred,
            "Conta Corrente",
            "Alimentação",
            "Despesa",
            description,
            amount,
            currency,
            rate,
            "EUR",
            "Manual");
}
