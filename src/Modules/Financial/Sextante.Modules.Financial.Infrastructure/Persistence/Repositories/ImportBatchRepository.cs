using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.ImportBatches;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class ImportBatchRepository : IImportBatchRepository
{
    private readonly FinancialDbContext _db;

    public ImportBatchRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public Task<ImportBatch?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<IReadOnlyList<ImportBatch>> ListAsync(CancellationToken ct)
        => await _db.ImportBatches
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

    public Task AddAsync(ImportBatch batch, CancellationToken ct)
        => _db.ImportBatches.AddAsync(batch, ct).AsTask();

    public void Update(ImportBatch batch) => _db.ImportBatches.Update(batch);

    public Task<int> SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}
