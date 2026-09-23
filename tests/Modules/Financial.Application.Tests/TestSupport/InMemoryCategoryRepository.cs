using Sextante.Modules.Financial.Domain.Categories;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.TestSupport;

/// <summary>
/// Repositório de categorias em memória para testes de handlers. Por
/// defeito responde a qualquer id com uma categoria do tipo
/// <see cref="DefaultKind"/> — os testes anteriores à Phase 6.5 usam ids
/// aleatórios e só precisam que a categoria "exista". Categorias
/// registadas com <see cref="With"/> têm prioridade; ids em
/// <see cref="Missing"/> devolvem <c>null</c>.
/// </summary>
public sealed class InMemoryCategoryRepository : ICategoryRepository
{
    private readonly Dictionary<Guid, Category> _known = new();

    public CategoryKind DefaultKind { get; init; } = CategoryKind.Expense;

    public HashSet<Guid> Missing { get; } = new();

    public InMemoryCategoryRepository With(Guid id, CategoryKind kind)
    {
        _known[id] = NewCategory(id, kind);
        return this;
    }

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        if (Missing.Contains(id))
        {
            return Task.FromResult<Category?>(null);
        }

        if (!_known.TryGetValue(id, out var category))
        {
            category = NewCategory(id, DefaultKind);
            _known[id] = category;
        }

        return Task.FromResult<Category?>(category);
    }

    public Task<Category?> GetByIdIncludingArchivedAsync(Guid id, TenantId tenantId, CancellationToken cancellationToken)
        => GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Category>>(_known.Values.ToList());

    public Task AddAsync(Category category, CancellationToken cancellationToken)
    {
        _known[category.Id] = category;
        return Task.CompletedTask;
    }

    public void Update(Category category) => _known[category.Id] = category;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

    public Task<int> CountActiveTransactionsAsync(Guid categoryId, CancellationToken cancellationToken)
        => Task.FromResult(0);

    public Task SeedAsync(Guid tenantId, IReadOnlyList<Category> categories, CancellationToken cancellationToken)
    {
        foreach (var category in categories)
        {
            _known[category.Id] = category;
        }

        return Task.CompletedTask;
    }

    private static Category NewCategory(Guid id, CategoryKind kind)
    {
        var category = Category.Create("Categoria", kind, "pi-tag", "#64748B", TenantId.New());
        typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, id);
        return category;
    }
}
