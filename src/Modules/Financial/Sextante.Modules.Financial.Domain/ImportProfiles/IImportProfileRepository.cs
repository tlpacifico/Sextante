namespace Sextante.Modules.Financial.Domain.ImportProfiles;

public interface IImportProfileRepository
{
    Task<ImportProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ImportProfile>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(ImportProfile profile, CancellationToken cancellationToken);
    void Update(ImportProfile profile);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
