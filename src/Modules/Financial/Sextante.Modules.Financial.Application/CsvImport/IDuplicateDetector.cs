using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.CsvImport;

public interface IDuplicateDetector
{
    Task<IReadOnlyList<DuplicateMatch>> FindPotentialDuplicatesAsync(
        IReadOnlyList<ParsedTransaction> previewRows,
        TenantId tenantId,
        CancellationToken ct);
}

public sealed record ParsedTransaction(
    int RowIndex,
    DateOnly Date,
    decimal Amount,
    string Currency,
    string Description,
    Guid AccountId);

public sealed record DuplicateMatch(
    int RowIndex,
    Guid ExistingTransactionId);
