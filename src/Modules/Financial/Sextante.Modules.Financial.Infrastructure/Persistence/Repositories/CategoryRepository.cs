using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.Categories;

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Repositories;

public sealed class CategoryRepository : ICategoryRepository
{
    private readonly FinancialDbContext _db;

    public CategoryRepository(FinancialDbContext db)
    {
        _db = db;
    }

    public async Task SeedAsync(
        Guid tenantId,
        IReadOnlyList<Category> categories,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // set_config(.., true) limita o scope ao TX. RLS WITH CHECK aceita.
        await _db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, true)",
            new object[] { tenantId.ToString() },
            cancellationToken);

        foreach (var category in categories)
        {
            await _db.Categories.AddAsync(category, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
        => await _db.Categories
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public Task AddAsync(Category category, CancellationToken cancellationToken)
        => _db.Categories.AddAsync(category, cancellationToken).AsTask();

    public void Update(Category category) => _db.Categories.Update(category);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _db.SaveChangesAsync(cancellationToken);

    public Task<int> CountActiveTransactionsAsync(Guid categoryId, CancellationToken cancellationToken)
        => _db.Transactions.CountAsync(t => t.CategoryId == categoryId, cancellationToken);
}
