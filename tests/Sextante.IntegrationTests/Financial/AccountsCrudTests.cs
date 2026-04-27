using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

public sealed class AccountsCrudTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public AccountsCrudTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Full_crud_lifecycle_is_observable_for_authenticated_tenant()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "acc");

        // List inicial: vazio.
        var initialList = await client.GetFromJsonAsync<List<AccountRow>>("/api/financial/accounts");
        initialList.Should().NotBeNull().And.BeEmpty();

        // Create.
        var createResponse = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta Principal",
            type = 0, // Checking
            openingBalanceAmount = 1000m,
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<AccountRow>();
        created!.Name.Should().Be("Conta Principal");

        // Update.
        var updateResponse = await client.PutAsJsonAsync($"/api/financial/accounts/{created.Id}", new
        {
            name = "Conta Renomeada",
            type = 0,
        });
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<AccountRow>();
        updated!.Name.Should().Be("Conta Renomeada");

        // List: 1 conta.
        var midList = await client.GetFromJsonAsync<List<AccountRow>>("/api/financial/accounts");
        midList.Should().HaveCount(1);

        // Archive (soft-delete).
        var deleteResponse = await client.DeleteAsync($"/api/financial/accounts/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // List depois de arquivar: vazio (filtro global filtra DeletedAt != null).
        var finalList = await client.GetFromJsonAsync<List<AccountRow>>("/api/financial/accounts");
        finalList.Should().BeEmpty();
    }

    private sealed record AccountRow(Guid Id, string Name, int Type);
}
