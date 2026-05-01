using System.Text.Json;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.ImportBatches;

public sealed class ImportBatch : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int FileNameMaxLength = 256;

    private ImportBatch()
    {
        FileName = null!;
        ParsedPreviewJson = "[]";
        CategorizationResultJson = "{}";
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public Guid? ImportProfileId { get; private set; }
    public string FileName { get; private set; }
    public ImportBatchStatus Status { get; private set; }
    public int TotalRows { get; private set; }
    public int ImportedRows { get; private set; }
    public int DuplicateRows { get; private set; }
    public int ErrorRows { get; private set; }

    public string ParsedPreviewJson { get; set; }
    public bool PreviewTruncated { get; private set; }

    public string CategorizationResultJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static ImportBatch StartParsing(
        string fileName,
        TenantId tenantId,
        Guid? importProfileId = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ImportBatchFileNameRequiredException();
        if (fileName.Length > FileNameMaxLength)
            throw new ImportBatchFileNameTooLongException(FileNameMaxLength);

        return new ImportBatch
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            ImportProfileId = importProfileId,
            FileName = Path.GetFileName(fileName),
            Status = ImportBatchStatus.Parsing,
        };
    }

    public void SetPreview(
        int totalRows,
        string parsedPreviewJson,
        bool truncated,
        int errorRows)
    {
        TotalRows = totalRows;
        ParsedPreviewJson = parsedPreviewJson;
        PreviewTruncated = truncated;
        ErrorRows = errorRows;
        Status = ImportBatchStatus.PreviewReady;
    }

    public void MarkConfirming(int duplicateRows)
    {
        DuplicateRows = duplicateRows;
        Status = ImportBatchStatus.Confirming;
    }

    public void MarkImporting()
    {
        Status = ImportBatchStatus.Importing;
    }

    public void Complete(
        int importedRows,
        int autoCategorizedCount,
        int manualCount)
    {
        ImportedRows = importedRows;
        Status = ImportBatchStatus.Completed;

        CategorizationResultJson = JsonSerializer.Serialize(new
        {
            autoCategorized = autoCategorizedCount,
            manual = manualCount,
            uncategorized = importedRows - autoCategorizedCount - manualCount,
        });
    }

    public void Fail(int errorRows)
    {
        ErrorRows = errorRows;
        Status = ImportBatchStatus.Failed;
    }

    public void Archive() => DeletedAt = DateTimeOffset.UtcNow;
}
