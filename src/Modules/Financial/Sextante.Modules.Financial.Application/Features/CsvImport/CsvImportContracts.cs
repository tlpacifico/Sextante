namespace Sextante.Modules.Financial.Application.Features.CsvImport;

public sealed record UploadCsvResponse(
    Guid BatchId,
    IReadOnlyList<string> Headers,
    IReadOnlyList<PreviewRowDto> PreviewRows,
    int TotalRowCount,
    bool Truncated,
    string DetectedDelimiter,
    bool DetectedHasHeader,
    IReadOnlyList<string> Errors);

public sealed record PreviewRowDto(
    int RowIndex,
    IReadOnlyList<string> Values,
    bool IsDuplicate,
    Guid? DuplicateTransactionId,
    string? SuggestedCategoryName,
    Guid? SuggestedCategoryId,
    bool IsAutoCategorized,
    string? Error);

public sealed record UpdatePreviewCommand(
    Guid BatchId,
    IReadOnlyList<ColumnMappingInput> ColumnMappings,
    string? Delimiter,
    bool? HasHeaderRow,
    string? DateFormat,
    string? DecimalSeparator,
    int? SkipRows);

public sealed record ColumnMappingInput(
    string CsvColumnName,
    string? TransactionField);

public sealed record ConfirmImportCommand(
    Guid BatchId,
    IReadOnlyList<Guid> IncludeDuplicates);

public sealed record ListImportBatchesQuery();

public sealed record ImportBatchResponse(
    Guid Id,
    Guid? ImportProfileId,
    string FileName,
    string Status,
    int TotalRows,
    int ImportedRows,
    int DuplicateRows,
    int ErrorRows,
    string CreatedAt);
