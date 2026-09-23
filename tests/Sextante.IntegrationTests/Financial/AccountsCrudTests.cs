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

    [Fact]
    public async Task Archiving_an_account_with_active_transactions_is_rejected()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "acc-arch");

        var createResponse = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta com movimentos",
            type = 0,
            openingBalanceAmount = 0m,
        });
        var account = await createResponse.Content.ReadFromJsonAsync<AccountRow>();

        var categoryResponse = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name = "Supermercado",
            kind = 0,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });
        var category = await categoryResponse.Content.ReadFromJsonAsync<IdRow>();

        var txResponse = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId = account!.Id,
            categoryId = category!.Id,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            amount = 12m,
            currency = (string?)null,
            description = "compra",
            tags = (string[]?)null,
        });
        txResponse.EnsureSuccessStatusCode();

        var deleteResponse = await client.DeleteAsync($"/api/financial/accounts/{account.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await deleteResponse.Content.ReadAsStringAsync()).Should().Contain("transações ativas");
        (await client.GetAsync($"/api/financial/accounts/{account.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Changing_type_of_credit_card_with_negative_opening_balance_is_rejected_with_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "acc-type");

        var createResponse = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Cartão Ouro",
            type = 3, // CreditCard
            openingBalanceAmount = -100m,
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<AccountRow>();

        var updateResponse = await client.PutAsJsonAsync($"/api/financial/accounts/{created!.Id}", new
        {
            name = "Cartão Ouro",
            type = 0, // Checking
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await updateResponse.Content.ReadFromJsonAsync<ValidationProblemRow>();
        body!.Title.Should().Be("Erros de validação");
        body.Errors.Should().ContainKey("account");
    }

    [Fact]
    public async Task Creating_account_with_opening_balance_date_in_the_future_is_rejected_with_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "acc-future");
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var createResponse = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta do futuro",
            type = 0,
            openingBalanceAmount = 0m,
            openingBalanceDate = tomorrow,
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record IdRow(Guid Id);

    private sealed record AccountRow(Guid Id, string Name, string Type);

    private sealed record ValidationProblemRow(string Title, Dictionary<string, string[]> Errors);
}
