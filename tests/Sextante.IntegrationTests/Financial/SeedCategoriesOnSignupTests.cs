using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

public sealed class SeedCategoriesOnSignupTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public SeedCategoriesOnSignupTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task After_signup_tenant_has_eleven_seed_categories()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "seed");

        // O subscriber Wolverine corre em background — esperar até 5s
        // pelo seed propagar antes de falhar.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        List<CategoryRow>? categories = null;
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync("/api/financial/categories");
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(
                    $"GET /api/financial/categories falhou ({(int)response.StatusCode}): {body}");
            }
            categories = await response.Content.ReadFromJsonAsync<List<CategoryRow>>();
            if (categories is not null && categories.Count >= 11)
            {
                break;
            }
            await Task.Delay(100);
        }

        categories.Should().NotBeNull();
        var resolved = categories!;
        resolved.Should().HaveCount(11);
        resolved.Count(c => c.Kind == "Expense").Should().Be(7, "Expense categories");
        resolved.Count(c => c.Kind == "Income").Should().Be(4, "Income categories");
        resolved.Should().Contain(c => c.Name == "Salário");
        resolved.Should().Contain(c => c.Name == "Alimentação");
    }

    private sealed record CategoryRow(
        Guid Id,
        string Name,
        string Kind,
        string IconName,
        string ColorHex);
}
