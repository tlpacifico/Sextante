using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6 (grupo 1.1) — os filtros de descrição, tipo (kind da
/// categoria) e intervalo de valor eram aplicados client-side em
/// <c>transactions.page.ts</c>, logo só afetavam a página corrente.
/// Estes testes provam que passaram a ser aplicados no servidor: com
/// <c>pageSize</c> pequeno, o filtro tem de atravessar a paginação.
/// </summary>
public sealed class TransactionFilteringTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public TransactionFilteringTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Filters_by_description_across_pages()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "fdesc");
        var account = await CreateAccount(client);
        var category = await CreateCategory(client);

        await PostTransaction(client, account, category, 10m, "Supermercado Pingo");
        await PostTransaction(client, account, category, 20m, "Combustível BP");
        await PostTransaction(client, account, category, 30m, "Supermercado Lidl");
        await PostTransaction(client, account, category, 40m, "Farmácia");
        await PostTransaction(client, account, category, 50m, "supermercado continente");

        var firstPage = await client.GetFromJsonAsync<TransactionsPage>(
            "/api/financial/transactions?descriptionContains=supermercado&pageSize=2");

        firstPage!.Items.Should().HaveCount(2);
        firstPage.NextCursor.Should().NotBeNull(
            "com 3 matches e pageSize=2 o filtro tem de ser aplicado antes da paginação");

        var secondPage = await client.GetFromJsonAsync<TransactionsPage>(
            $"/api/financial/transactions?descriptionContains=supermercado&pageSize=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");

        secondPage!.Items.Should().HaveCount(1);

        var all = firstPage.Items.Concat(secondPage.Items).ToList();
        all.Should().AllSatisfy(t =>
            t.Description!.ToLowerInvariant().Should().Contain("supermercado"));
    }

    [Fact]
    public async Task Filters_by_kind_of_category()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "fkind");
        var account = await CreateAccount(client);
        var expense = await CreateCategory(client, kind: 0);
        var income = await CreateCategory(client, kind: 1, name: "Salário");

        await PostTransaction(client, account, expense, 10m, "despesa 1");
        await PostTransaction(client, account, expense, 20m, "despesa 2");
        await PostTransaction(client, account, income, 900m, "salário");

        var page = await client.GetFromJsonAsync<TransactionsPage>(
            "/api/financial/transactions?kind=Income&pageSize=50");

        page!.Items.Should().HaveCount(1);
        page.Items[0].CategoryId.Should().Be(income);
    }

    [Fact]
    public async Task Filters_by_amount_range()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "famount");
        var account = await CreateAccount(client);
        var category = await CreateCategory(client);

        await PostTransaction(client, account, category, 5m, "cinco");
        await PostTransaction(client, account, category, 50m, "cinquenta");
        await PostTransaction(client, account, category, 500m, "quinhentos");

        var page = await client.GetFromJsonAsync<TransactionsPage>(
            "/api/financial/transactions?amountMin=10&amountMax=100&pageSize=50");

        page!.Items.Should().HaveCount(1);
        page.Items[0].Amount.Amount.Should().Be(50m);
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

    private static async Task PostTransaction(
        HttpClient client,
        Guid accountId,
        Guid categoryId,
        decimal amount,
        string description)
    {
        var response = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-(double)amount),
            amount,
            description,
            tags = (string[]?)null,
        });
        response.EnsureSuccessStatusCode();
    }

    private sealed record IdRow(Guid Id);
    private sealed record MoneyValue(decimal Amount, string Currency);
    private sealed record TransactionRow(Guid Id, Guid CategoryId, MoneyValue Amount, string? Description);
    private sealed record TransactionsPage(List<TransactionRow> Items, string? NextCursor);
}
