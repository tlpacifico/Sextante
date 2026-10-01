using System.Globalization;
using Sextante.Modules.Financial.Application.Features.CsvImport;

namespace Sextante.Modules.Financial.Application.StatementConversion;

public sealed record CanonicalCsv(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    ImportParseSettings Settings);

/// <summary>
/// Transforma o resultado de um conversor nas linhas canónicas do import
/// (<c>Data Lanc.;Data Valor;Descrição;Valor;Saldo</c>, <c>dd/MM/yyyy</c>,
/// decimal vírgula) para reutilizar preview, dedup, regras e confirm sem os
/// alterar (D2).
/// </summary>
public static class CanonicalCsvAdapter
{
    public const string DateFormat = "dd/MM/yyyy";

    public static readonly IReadOnlyList<string> Headers =
        ["Data Lanc.", "Data Valor", "Descrição", "Valor", "Saldo"];

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static CanonicalCsv Adapt(StatementConversionResult result)
    {
        var rows = result.Rows
            .Select(r => (IReadOnlyList<string>)new[]
            {
                r.Date.ToString(DateFormat, Culture),
                r.ValueDate.ToString(DateFormat, Culture),
                SanitizeDescription(r.Description),
                FormatAmount(r.SignedAmount),
                r.Balance is { } balance ? FormatAmount(balance) : string.Empty,
            })
            .ToList();

        var settings = new ImportParseSettings(
            [
                new ColumnMappingInput("Data Lanc.", "Date"),
                new ColumnMappingInput("Descrição", "Description"),
                new ColumnMappingInput("Valor", "Amount"),
            ],
            DateFormat,
            ",");

        return new CanonicalCsv(Headers, rows, settings);
    }

    /// <summary>Linhas em CSV ficam como as dos scripts: sem <c>;</c> nem aspas duplas.</summary>
    public static string SanitizeDescription(string description)
        => string.Join(' ', description.Replace(';', ',').Replace('"', '\'')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string FormatAmount(decimal value)
        => value.ToString("0.00", Culture).Replace('.', ',');
}
