using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

public sealed class RecurringRulesCrudTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public RecurringRulesCrudTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Full_crud_lifecycle_for_recurring_rule()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rr");

        var accountId = await CreateAccount(client);
        var categoryId = await CreateCategory(client);

        // List inicial: vazio.
        var initialList = await client.GetFromJsonAsync<List<RuleRow>>("/api/financial/recurring-rules");
        initialList.Should().NotBeNull().And.BeEmpty();

        // Create.
        var createResponse = await client.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Netflix",
            amount = 12.99m,
            currency = "EUR",
            accountId,
            categoryId,
            frequency = "Monthly",
            interval = 1,
            startDate = DateOnly.Parse("2026-06-01"),
            endDate = (DateOnly?)null,
            tags = new[] { "fixa" },
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<RuleRow>();
        created!.Description.Should().Be("Netflix");
        created.Frequency.Should().Be("Monthly");
        var ruleId = created.Id;

        // Get by ID.
        var getResponse = await client.GetAsync($"/api/financial/recurring-rules/{ruleId}");
        getResponse.EnsureSuccessStatusCode();
        var fetched = await getResponse.Content.ReadFromJsonAsync<RuleRow>();
        fetched!.Id.Should().Be(ruleId);
        fetched.Description.Should().Be("Netflix");

        // Update.
        var updateResponse = await client.PutAsJsonAsync($"/api/financial/recurring-rules/{ruleId}", new
        {
            description = "Netflix Premium",
            amount = 12.99m,
            currency = "EUR",
            accountId,
            categoryId,
            frequency = "Monthly",
            interval = 1,
            startDate = DateOnly.Parse("2026-06-01"),
            endDate = (DateOnly?)null,
            isActive = true,
            tags = new[] { "fixa" },
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<RuleRow>();
        updated!.Description.Should().Be("Netflix Premium");

        // List: tem 1 regra.
        var midList = await client.GetFromJsonAsync<List<RuleRow>>("/api/financial/recurring-rules");
        midList.Should().HaveCount(1);

        // Archive (soft-delete).
        var deleteResponse = await client.DeleteAsync($"/api/financial/recurring-rules/{ruleId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // List depois de arquivar: vazio (filtro de soft-delete).
        var finalList = await client.GetFromJsonAsync<List<RuleRow>>("/api/financial/recurring-rules");
        finalList.Should().BeEmpty();

        // Get by ID depois de arquivar: 404.
        var getAfterArchive = await client.GetAsync($"/api/financial/recurring-rules/{ruleId}");
        getAfterArchive.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Upcoming_occurrences_returns_dates()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "up");

        var accountId = await CreateAccount(client);
        var categoryId = await CreateCategory(client);

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var createResponse = await client.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Daily rule",
            amount = 5m,
            currency = "EUR",
            accountId,
            categoryId,
            frequency = "Daily",
            interval = 1,
            startDate = tomorrow,
            endDate = (DateOnly?)null,
            tags = (string[]?)null,
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<RuleRow>();
        var ruleId = created!.Id;

        var upcomingResponse = await client.GetAsync($"/api/financial/recurring-rules/{ruleId}/upcoming?count=3");
        upcomingResponse.EnsureSuccessStatusCode();
        var dates = await upcomingResponse.Content.ReadFromJsonAsync<List<DateOnly>>();
        dates.Should().HaveCount(3);
        dates![0].Should().Be(tomorrow);
    }

    [Fact]
    public async Task Upcoming_occurrences_for_non_existent_rule_returns_404()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "nx");

        var nonExistent = Guid.NewGuid();
        var response = await client.GetAsync($"/api/financial/recurring-rules/{nonExistent}/upcoming");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_with_nonexistent_account_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ne");

        var response = await client.PostAsJsonAsync("/api/financial/recurring-rules", new
        {
            description = "Invalid rule",
            amount = 10m,
            currency = "EUR",
            accountId = Guid.NewGuid(),
            categoryId = (Guid?)null,
            frequency = "Monthly",
            interval = 1,
            startDate = DateOnly.Parse("2026-06-01"),
            endDate = (DateOnly?)null,
            tags = (string[]?)null,
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> CreateAccount(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta Teste",
            type = 0,
            openingBalanceAmount = 0m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<Guid> CreateCategory(HttpClient client, int kind = 0, string name = "Despesa Teste")
    {
        var response = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name,
            kind,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private sealed record IdRow(Guid Id);
    private sealed record RuleRow(Guid Id, string Description, string Frequency, int Interval, bool IsActive, Guid AccountId, Guid? CategoryId);
}
