namespace Sextante.Modules.Financial.Domain.ImportProfiles;

public interface IImportProfileRepository
{
    Task<ImportProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ImportProfile>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(ImportProfile profile, CancellationToken cancellationToken);
    void Update(ImportProfile profile);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Grava o perfil fora de uma request HTTP (subscriber do signup),
    /// definindo <c>app.current_tenant_id</c> para a duração da transação.
    /// </summary>
    Task SeedAsync(Guid tenantId, ImportProfile profile, CancellationToken cancellationToken);
}
