using FluentAssertions;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.ImportBatches;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.ImportBatchesSpec;

public sealed class ImportBatchTests
{
    private static readonly TenantId Tenant = TenantId.New();

    [Fact]
    public void StartParsing_initializes_with_parsing_status()
    {
        var batch = ImportBatch.StartParsing("extrato.csv", Tenant);

        batch.Status.Should().Be(ImportBatchStatus.Parsing);
        batch.FileName.Should().Be("extrato.csv");
        batch.TotalRows.Should().Be(0);
        batch.ImportedRows.Should().Be(0);
        batch.DuplicateRows.Should().Be(0);
        batch.ErrorRows.Should().Be(0);
        batch.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void StartParsing_strips_directory_path_from_filename()
    {
        var batch = ImportBatch.StartParsing("/tmp/uploads/extrato.csv", Tenant);
        batch.FileName.Should().Be("extrato.csv");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StartParsing_rejects_empty_filename(string fileName)
    {
        var act = () => ImportBatch.StartParsing(fileName, Tenant);
        act.Should().Throw<ImportBatchFileNameRequiredException>();
    }

    [Fact]
    public void StartParsing_rejects_filename_above_max_length()
    {
        var fileName = new string('a', ImportBatch.FileNameMaxLength + 1) + ".csv";
        var act = () => ImportBatch.StartParsing(fileName, Tenant);
        act.Should().Throw<ImportBatchFileNameTooLongException>();
    }

    [Fact]
    public void SetPreview_transitions_to_preview_ready_and_records_counts()
    {
        var batch = ImportBatch.StartParsing("x.csv", Tenant);

        batch.SetPreview(totalRows: 100, parsedPreviewJson: "[]", truncated: false, errorRows: 3);

        batch.Status.Should().Be(ImportBatchStatus.PreviewReady);
        batch.TotalRows.Should().Be(100);
        batch.ErrorRows.Should().Be(3);
        batch.PreviewTruncated.Should().BeFalse();
        batch.ParsedPreviewJson.Should().Be("[]");
    }

    [Fact]
    public void MarkConfirming_records_duplicate_rows_and_advances_status()
    {
        var batch = ImportBatch.StartParsing("x.csv", Tenant);
        batch.SetPreview(10, "[]", false, 0);

        batch.MarkConfirming(duplicateRows: 4);

        batch.Status.Should().Be(ImportBatchStatus.Confirming);
        batch.DuplicateRows.Should().Be(4);
    }

    [Fact]
    public void MarkImporting_advances_status()
    {
        var batch = ImportBatch.StartParsing("x.csv", Tenant);
        batch.SetPreview(10, "[]", false, 0);
        batch.MarkConfirming(0);

        batch.MarkImporting();

        batch.Status.Should().Be(ImportBatchStatus.Importing);
    }

    [Fact]
    public void Complete_advances_to_completed_and_records_imported_rows()
    {
        var batch = ImportBatch.StartParsing("x.csv", Tenant);
        batch.SetPreview(10, "[]", false, 0);
        batch.MarkConfirming(0);
        batch.MarkImporting();

        batch.Complete(importedRows: 8, autoCategorizedCount: 5, manualCount: 2);

        batch.Status.Should().Be(ImportBatchStatus.Completed);
        batch.ImportedRows.Should().Be(8);
        batch.CategorizationResultJson.Should().Contain("\"autoCategorized\":5");
        batch.CategorizationResultJson.Should().Contain("\"manual\":2");
        batch.CategorizationResultJson.Should().Contain("\"uncategorized\":1");
    }

    [Fact]
    public void Fail_advances_to_failed_and_records_error_rows()
    {
        var batch = ImportBatch.StartParsing("x.csv", Tenant);

        batch.Fail(errorRows: 7);

        batch.Status.Should().Be(ImportBatchStatus.Failed);
        batch.ErrorRows.Should().Be(7);
    }

    [Fact]
    public void Archive_sets_deleted_at()
    {
        var batch = ImportBatch.StartParsing("x.csv", Tenant);
        batch.DeletedAt.Should().BeNull();
        batch.Archive();
        batch.DeletedAt.Should().NotBeNull();
    }
}
