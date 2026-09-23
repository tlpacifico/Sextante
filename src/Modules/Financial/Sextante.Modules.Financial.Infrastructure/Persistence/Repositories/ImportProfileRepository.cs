using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.ImportProfiles;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class ImportProfileRepository : IImportProfileRepository
{
    private readonly FinancialDbContext _db;

    public ImportProfileRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public Task<ImportProfile?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.ImportProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<ImportProfile>> ListAsync(CancellationToken ct)
        => await _db.ImportProfiles.OrderBy(p => p.Name).ToListAsync(ct);

    public Task AddAsync(ImportProfile profile, CancellationToken ct)
        => _db.ImportProfiles.AddAsync(profile, ct).AsTask();

    public void Update(ImportProfile profile) => _db.ImportProfiles.Update(profile);

    public Task<int> SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);

    public async Task SeedAsync(Guid tenantId, ImportProfile profile, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // set_config(.., true) limita o scope ao TX. RLS WITH CHECK aceita.
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, true)",
            new object[] { tenantId.ToString() },
            ct);

        await _db.ImportProfiles.AddAsync(profile, ct);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
