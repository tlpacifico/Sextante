namespace Sextante.Modules.Financial.Domain.Categories;

/// <summary>
/// Allowlist de PrimeIcons aceites como <see cref="Category.IconName"/>.
/// Mantém em sincronia manual com <c>category-icons.ts</c> no frontend.
/// </summary>
public static class AllowedCategoryIcons
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "pi-shopping-cart",
        "pi-car",
        "pi-money-bill",
        "pi-heart",
        "pi-home",
        "pi-book",
        "pi-briefcase",
        "pi-gift",
        "pi-globe",
        "pi-graduation-cap",
        "pi-coffee",
        "pi-credit-card",
        "pi-wallet",
        "pi-chart-line",
        "pi-bolt",
        "pi-phone",
        "pi-shield",
        "pi-tag",
        "pi-utensils",
        "pi-plane",
        "pi-sun",
        "pi-star",
        "pi-cog",
        "pi-bookmark",
    };
}
