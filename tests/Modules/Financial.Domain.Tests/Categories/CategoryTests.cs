using FluentAssertions;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Tests.CategoriesSpec;

public sealed class CategoryTests
{
    private static readonly TenantId Tenant = TenantId.New();

    [Fact]
    public void Create_rejects_icon_outside_allowlist()
    {
        var act = () => Category.Create("Casa", CategoryKind.Expense, "pi-foo", "#112233", Tenant);
        act.Should().Throw<InvalidIconNameException>();
    }

    [Fact]
    public void Create_rejects_invalid_color_hex()
    {
        var act = () => Category.Create("Casa", CategoryKind.Expense, "pi-home", "blue", Tenant);
        act.Should().Throw<InvalidColorHexException>();
    }

    [Fact]
    public void Create_succeeds_with_allowed_icon_and_hex()
    {
        var category = Category.Create("Casa", CategoryKind.Expense, "pi-home", "#0EA5E9", Tenant);
        category.Name.Should().Be("Casa");
        category.IconName.Should().Be("pi-home");
        category.Kind.Should().Be(CategoryKind.Expense);
    }

    [Fact]
    public void EnsureCanArchive_is_silent_when_no_active_transactions()
    {
        var category = Category.Create("Casa", CategoryKind.Expense, "pi-home", "#0EA5E9", Tenant);
        var act = () => category.EnsureCanArchive(0);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureCanArchive_throws_when_active_transactions_exist()
    {
        var category = Category.Create("Casa", CategoryKind.Expense, "pi-home", "#0EA5E9", Tenant);
        var act = () => category.EnsureCanArchive(3);
        act.Should().Throw<CategoryHasActiveTransactionsException>();
    }
}
