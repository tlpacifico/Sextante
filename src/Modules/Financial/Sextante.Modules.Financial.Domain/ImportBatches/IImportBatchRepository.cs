namespace Sextante.Modules.Financial.Domain.ImportBatches;

public interface IImportBatchRepository
{
    Task<ImportBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ImportBatch>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(ImportBatch batch, CancellationToken cancellationToken);
    void Update(ImportBatch batch);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
