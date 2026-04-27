using FluentAssertions;
using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Domain.Categories;

namespace Sextante.Modules.Financial.Application.Tests.Common;

public sealed class DefaultCategoriesTests
{
    [Fact]
    public void All_has_eleven_entries()
    {
        DefaultCategories.All.Should().HaveCount(11);
    }

    [Fact]
    public void All_has_seven_expense_and_four_income()
    {
        DefaultCategories.All.Count(c => c.Kind == CategoryKind.Expense).Should().Be(7);
        DefaultCategories.All.Count(c => c.Kind == CategoryKind.Income).Should().Be(4);
    }

    [Fact]
    public void All_uses_only_allowed_icons()
    {
        foreach (var seed in DefaultCategories.All)
        {
            AllowedCategoryIcons.All.Should().Contain(seed.IconName,
                $"icon '{seed.IconName}' must be in allowlist");
        }
    }
}
