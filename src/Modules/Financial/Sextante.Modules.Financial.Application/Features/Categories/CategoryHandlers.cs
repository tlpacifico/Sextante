using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.Categories;

[NonTransactional]
public static class CategoryHandlers
{
    public static async Task<CategoryResponse> Handle(
        CreateCategoryCommand command,
        ICategoryRepository repository,
        ITenantContext tenant,
        CancellationToken cancellationToken)
    {
        var category = Category.Create(
            command.Name,
            command.Kind,
            command.IconName,
            command.ColorHex,
            tenant.TenantId);

        await repository.AddAsync(category, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return ToResponse(category);
    }

    public static async Task<CategoryResponse?> Handle(
        UpdateCategoryCommand command,
        ICategoryRepository repository,
        CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (category is null)
        {
            return null;
        }

        category.Update(command.Name, command.IconName, command.ColorHex);
        repository.Update(category);
        await repository.SaveChangesAsync(cancellationToken);
        return ToResponse(category);
    }

    public static async Task<ArchiveCategoryResult> Handle(
        ArchiveCategoryCommand command,
        ICategoryRepository repository,
        CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (category is null)
        {
            return ArchiveCategoryResult.NotFound;
        }

        var activeCount = await repository.CountActiveTransactionsAsync(category.Id, cancellationToken);
        category.EnsureCanArchive(activeCount);
        category.Archive();
        repository.Update(category);
        await repository.SaveChangesAsync(cancellationToken);
        return ArchiveCategoryResult.Archived;
    }

    public static async Task<CategoryResponse?> Handle(
        GetCategoryByIdQuery query,
        ICategoryRepository repository,
        CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(query.Id, cancellationToken);
        return category is null ? null : ToResponse(category);
    }

    public static async Task<IReadOnlyList<CategoryResponse>> Handle(
        ListCategoriesQuery query,
        ICategoryRepository repository,
        CancellationToken cancellationToken)
    {
        var categories = await repository.ListAsync(cancellationToken);
        return categories.Select(ToResponse).ToList();
    }

    private static CategoryResponse ToResponse(Category category)
        => new(
            category.Id,
            category.Name,
            category.Kind,
            category.IconName,
            category.ColorHex,
            category.CreatedAt,
            category.UpdatedAt);
}

public enum ArchiveCategoryResult
{
    NotFound,
    Archived,
}
