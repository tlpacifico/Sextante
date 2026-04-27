namespace Sextante.Modules.Financial.Domain.Categories;

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);
    Task AddAsync(Category category, CancellationToken cancellationToken);
    void Update(Category category);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<int> CountActiveTransactionsAsync(Guid categoryId, CancellationToken cancellationToken);

    /// <summary>
    /// Insere categorias para um tenant explícito (usado pelo subscriber
    /// <c>UserRegisteredIntegrationEvent</c>, que corre fora de um HTTP
    /// scope com tenant resolvido). A implementação overrides o GUC
    /// <c>app.current_tenant_id</c> para a duração da transação.
    /// </summary>
    Task SeedAsync(
        Guid tenantId,
        IReadOnlyList<Category> categories,
        CancellationToken cancellationToken);
}
