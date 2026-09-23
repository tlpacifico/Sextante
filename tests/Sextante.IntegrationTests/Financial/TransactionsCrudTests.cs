using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

public sealed class TransactionsCrudTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public TransactionsCrudTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_transactions_returns_filtered_page()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "txn");

        var account = await CreateAccount(client);
        var category = await CreateCategory(client);

        for (int i = 0; i < 3; i++)
        {
            await client.PostAsJsonAsync("/api/financial/transactions", new
            {
                accountId = account,
                categoryId = category,
                occurredAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                amount = 10m + i,
                description = $"t{i}",
                tags = (string[]?)null,
            });
        }

        var page = await client.GetFromJsonAsync<TransactionsPage>("/api/financial/transactions?pageSize=10");
        page!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Summary_returns_totals_for_filter()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "sum");

        var account = await CreateAccount(client);
        var expenseCategory = await CreateCategory(client, kind: 0);
        var incomeCategory = await CreateCategory(client, kind: 1, name: "Receita");

        await PostTransaction(client, account, expenseCategory, 30m);
        await PostTransaction(client, account, incomeCategory, 100m);

        var summary = await client.GetFromJsonAsync<SummaryRow>("/api/financial/transactions/summary");
        summary!.Income.Amount.Should().Be(100m);
        summary.Expense.Amount.Should().Be(30m);
        summary.Net.Amount.Should().Be(70m);
    }

    [Fact]
    public async Task Response_exposes_direction_and_kind()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "tx-direction");
        var accountId = await CreateAccount(client);
        var income = await CreateCategory(client, kind: 1, name: "Salário");

        var response = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId,
            categoryId = income,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            amount = 1500m,
            currency = (string?)null,
            description = "Vencimento",
            tags = (string[]?)null,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        body.GetProperty("direction").GetString().Should().Be("Inflow");
        body.GetProperty("kind").GetString().Should().Be("Regular");
        body.GetProperty("transferId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        body.GetProperty("categoryId").GetGuid().Should().Be(income);
    }

    private static async Task<Guid> CreateAccount(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta",
            type = 0,
            openingBalanceAmount = 0m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<Guid> CreateCategory(HttpClient client, int kind = 0, string name = "Despesa")
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

    private static async Task PostTransaction(HttpClient client, Guid accountId, Guid categoryId, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            amount,
            description = (string?)null,
            tags = (string[]?)null,
        });
        response.EnsureSuccessStatusCode();
    }

    private sealed record IdRow(Guid Id);
    private sealed record TransactionsPage(List<IdRow> Items, string? NextCursor);
    private sealed record MoneyValue(decimal Amount, string Currency);
    private sealed record SummaryRow(MoneyValue Income, MoneyValue Expense, MoneyValue Net);
}
