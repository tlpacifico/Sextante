namespace Sextante.Modules.Financial.Application.CsvImport;

public interface ICsvParser
{
    CsvParseResult Parse(Stream csvStream, CsvParseOptions options, CancellationToken ct);
}

public sealed record CsvParseOptions(
    string? Delimiter = null,
    bool HasHeaderRow = true,
    int SkipRows = 0,
    int MaxPreviewRows = 1000);

public sealed record CsvParseResult(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    int TotalRowCount,
    bool Truncated);
