using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

public sealed class CategoriesCrudTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public CategoriesCrudTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_category_persists_with_tenant_id()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "catcrud");

        var createResponse = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name = "Combustível",
            kind = 0, // Expense
            iconName = "pi-car",
            colorHex = "#F97316",
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CategoryRow>();
        created!.Name.Should().Be("Combustível");
        created.Kind.Should().Be("Expense");
        created.IconName.Should().Be("pi-car");
    }

    [Fact]
    public async Task Archive_category_with_active_transaction_returns_400_with_PT_message()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "catarch");

        // 1. Criar conta.
        var accountResponse = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta",
            type = 0,
            openingBalanceAmount = 0m,
        });
        accountResponse.EnsureSuccessStatusCode();
        var account = await accountResponse.Content.ReadFromJsonAsync<IdRow>();

        // 2. Criar categoria.
        var categoryResponse = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name = "Compras",
            kind = 0,
            iconName = "pi-shopping-cart",
            colorHex = "#EF4444",
        });
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<IdRow>();

        // 3. Criar transação.
        var txResponse = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId = account!.Id,
            categoryId = category!.Id,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            amount = 12.5m,
            description = (string?)null,
            tags = (string[]?)null,
        });
        txResponse.EnsureSuccessStatusCode();

        // 4. Tentar arquivar a categoria — deve falhar com 400 + mensagem PT.
        var archiveResponse = await client.DeleteAsync($"/api/financial/categories/{category.Id}");
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await archiveResponse.Content.ReadAsStringAsync();
        body.Should().Contain("Não é possível arquivar uma categoria com transações ativas");
    }

    private sealed record CategoryRow(Guid Id, string Name, string Kind, string IconName, string ColorHex);
    private sealed record IdRow(Guid Id);
}
