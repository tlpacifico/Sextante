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

/// <summary>
/// Campos da Phase 6.5 (grupo 7) no fim e com default: o JSON de lotes já
/// gravados continua a deserializar (R9).
/// </summary>
public sealed record PreviewRowDto(
    int RowIndex,
    IReadOnlyList<string> Values,
    bool IsDuplicate,
    Guid? DuplicateTransactionId,
    string? SuggestedCategoryName,
    Guid? SuggestedCategoryId,
    bool IsAutoCategorized,
    string? Error,
    Guid? AccountId = null,
    bool IsBeforeOpeningBalance = false,
    string? TransferStatus = null,
    Guid? TransferTargetAccountId = null,
    string? TransferTargetAccountName = null,
    Guid? TransferCounterpartTransactionId = null);

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

/// <summary>
/// Phase 6.5 grupo 7 (Q3) — definições efetivas com que o preview foi
/// calculado, gravadas no lote para o confirm usar as mesmas.
/// <see cref="ColumnMappings"/> <c>null</c> = perfil/auto-deteção.
/// </summary>
public sealed record ImportParseSettings(
    IReadOnlyList<ColumnMappingInput>? ColumnMappings,
    string? DateFormat,
    string? DecimalSeparator);

public sealed record ConfirmImportCommand(
    Guid BatchId,
    IReadOnlyList<Guid> IncludeDuplicates,
    bool IncludeBeforeOpeningBalance = false);

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
