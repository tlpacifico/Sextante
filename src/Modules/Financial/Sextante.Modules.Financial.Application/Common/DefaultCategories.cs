using Sextante.Modules.Financial.Domain.Categories;

namespace Sextante.Modules.Financial.Application.Common;

/// <summary>
/// Lista canónica das 11 categorias seed criadas no signup
/// (per requirements §"Decisions"). Phase 3 introduz UI para
/// editar; Phase 2 entrega-as imutáveis no boot do tenant.
/// </summary>
public static class DefaultCategories
{
    public static readonly IReadOnlyList<DefaultCategory> All = new[]
    {
        new DefaultCategory("Alimentação", CategoryKind.Expense, "pi-utensils", "#EF4444"),
        new DefaultCategory("Transporte", CategoryKind.Expense, "pi-car", "#F97316"),
        new DefaultCategory("Saúde", CategoryKind.Expense, "pi-heart", "#EC4899"),
        new DefaultCategory("Lazer", CategoryKind.Expense, "pi-star", "#A855F7"),
        new DefaultCategory("Casa", CategoryKind.Expense, "pi-home", "#0EA5E9"),
        new DefaultCategory("Educação", CategoryKind.Expense, "pi-graduation-cap", "#6366F1"),
        new DefaultCategory("Outros", CategoryKind.Expense, "pi-tag", "#64748B"),
        new DefaultCategory("Salário", CategoryKind.Income, "pi-money-bill", "#10B981"),
        new DefaultCategory("Freelance", CategoryKind.Income, "pi-briefcase", "#22C55E"),
        new DefaultCategory("Investimentos", CategoryKind.Income, "pi-chart-line", "#14B8A6"),
        new DefaultCategory("Outros", CategoryKind.Income, "pi-bookmark", "#0F766E"),
    };
}

public sealed record DefaultCategory(string Name, CategoryKind Kind, string IconName, string ColorHex);
