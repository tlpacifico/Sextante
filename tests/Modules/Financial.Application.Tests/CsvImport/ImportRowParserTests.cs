using FluentAssertions;
using Sextante.Modules.Financial.Application.Features.CsvImport;
using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.CsvImport;

/// <summary>
/// Phase 6.5 grupo 7 — parse de uma linha do CSV com as definições do lote
/// (Q3) e direção pelo sinal (R1).
/// </summary>
public sealed class ImportRowParserTests
{
    private static readonly string[] Headers = ["Data", "Descrição", "Valor", "DC"];

    private static readonly ImportParseSettings Settings = new(
        [
            new ColumnMappingInput("Data", "Date"),
            new ColumnMappingInput("Descrição", "Description"),
            new ColumnMappingInput("Valor", "Amount"),
            new ColumnMappingInput("DC", "CreditDebitIndicator"),
        ],
        "dd/MM/yyyy",
        ",");

    private static ParsedImportRow? Parse(string date, string amount, string indicator, out string? error)
    {
        var resolver = ImportRowParser.ResolverFor(Headers, Settings, profile: null);
        return ImportRowParser.Parse([date, "PAGAMENTO", amount, indicator], resolver, Settings, out error);
    }

    [Fact]
    public void Negative_amount_with_comma_decimal_is_outflow()
    {
        var row = Parse("11/09/2026", "-1 901,30", "", out var error);

        error.Should().BeNull();
        row!.SignedAmount.Should().Be(-1901.30m);
        row.AbsAmount.Should().Be(1901.30m);
        row.Direction.Should().Be(TransactionDirection.Outflow);
        row.Date.Should().Be(new DateOnly(2026, 9, 11));
        row.Description.Should().Be("PAGAMENTO");
    }

    [Fact]
    public void Positive_amount_is_inflow()
    {
        Parse("14/09/2026", "450,00", "", out _)!.Direction.Should().Be(TransactionDirection.Inflow);
    }

    [Fact]
    public void Debit_indicator_turns_positive_amount_into_outflow()
    {
        var row = Parse("14/09/2026", "450,00", "D", out _);

        row!.Direction.Should().Be(TransactionDirection.Outflow);
        row.SignedAmount.Should().Be(-450m);
    }

    [Fact]
    public void Zero_amount_is_an_error()
    {
        Parse("14/09/2026", "0,00", "", out var error).Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Invalid_date_is_an_error()
    {
        Parse("31/31/2026", "10,00", "", out var error).Should().BeNull();
        error.Should().Contain("Data inválida");
    }

    [Fact]
    public void ResolverFor_prefers_saved_mappings_over_profile()
    {
        var profile = ImportProfile.Create(
            "Perfil", [new ColumnMapping("Valor", TransactionField.Date, null)], TenantId.New());

        var resolver = ImportRowParser.ResolverFor(Headers, Settings, profile);

        resolver.TryGetDate(["11/09/2026", "x", "10,00", ""], out var date).Should().BeTrue();
        date.Should().Be("11/09/2026");
    }

    [Fact]
    public void ResolverFor_without_saved_mappings_uses_profile()
    {
        var profile = ImportProfile.Create(
            "Perfil", [new ColumnMapping("Descrição", TransactionField.Date, null)], TenantId.New());

        var resolver = ImportRowParser.ResolverFor(Headers, settings: null, profile);

        resolver.TryGetDate(["11/09/2026", "desc", "10,00", ""], out var date).Should().BeTrue();
        date.Should().Be("desc");
    }
}
