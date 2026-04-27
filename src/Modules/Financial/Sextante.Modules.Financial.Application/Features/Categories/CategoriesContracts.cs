using Sextante.Modules.Financial.Domain.Categories;

namespace Sextante.Modules.Financial.Application.Features.Categories;

public sealed record CategoryResponse(
    Guid Id,
    string Name,
    CategoryKind Kind,
    string IconName,
    string ColorHex,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateCategoryCommand(
    string Name,
    CategoryKind Kind,
    string IconName,
    string ColorHex);

public sealed record UpdateCategoryCommand(
    Guid Id,
    string Name,
    string IconName,
    string ColorHex);

public sealed record ArchiveCategoryCommand(Guid Id);

public sealed record GetCategoryByIdQuery(Guid Id);

public sealed record ListCategoriesQuery();
