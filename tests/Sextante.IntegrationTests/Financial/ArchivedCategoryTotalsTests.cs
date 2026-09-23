using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §0.4 — o repositório juntava transações a categorias com o
/// filtro global de soft-delete ativo, pelo que transações de uma
/// categoria arquivada desapareciam do summary, do donut e do export.
/// </summary>
public sealed class ArchivedCategoryTotalsTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public ArchivedCategoryTotalsTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Summary_by_category_and_export_keep_transactions_of_archived_categories()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "arch-cat");
        var accountId = await CreateAccountAsync(client);
        var categoryId = await CreateExpenseCategoryAsync(client, "Temporária");
        await CreateTransactionAsync(client, accountId, categoryId, 40m);

        // A regra de domínio impede arquivar uma categoria com transações
        // ativas pela API; o estado existe em dados legados. Simular via DB.
        await using (var super = _fixture.OpenSuperuserConnection())
        await using (var cmd = new NpgsqlCommand(
            "UPDATE financial.categories SET deleted_at = now() WHERE id = @id",
            super))
        {
            cmd.Parameters.AddWithValue("id", categoryId);
            await cmd.ExecuteNonQueryAsync();
        }

        var range = "dateFrom=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"))
            + "&dateTo=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));

        var summary = await client.GetFromJsonAsync<JsonElement>(
            $"/api/financial/transactions/summary?{range}");
        summary.GetProperty("expense").GetProperty("amount").GetDecimal().Should().Be(40m);

        var byCategory = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/financial/transactions/by-category?kind=Expense&{range}");
        byCategory.Should().Contain(r => r.GetProperty("categoryName").GetString() == "Temporária");

        var expenses = await client.GetFromJsonAsync<JsonElement>(
            $"/api/financial/transactions?kind=Expense&{range}");
        expenses.GetProperty("items").GetArrayLength().Should().Be(1);

        var csv = await client.GetStringAsync($"/api/financial/transactions/export?{range}");
        csv.Should().Contain("Temporária");
    }

    [Fact]
    public async Task Archived_category_of_another_tenant_never_names_my_transactions()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "arch-cat-a");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "arch-cat-b");

        var accountA = await CreateAccountAsync(clientA);
        var categoryA = await CreateExpenseCategoryAsync(clientA, "Só do A");
        await CreateTransactionAsync(clientA, accountA, categoryA, 15m);
        await CreateExpenseCategoryAsync(clientB, "Só do B");

        await using (var super = _fixture.OpenSuperuserConnection())
        await using (var cmd = new NpgsqlCommand(
            "UPDATE financial.categories SET deleted_at = now() WHERE name IN ('Só do A', 'Só do B')",
            super))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var range = "dateFrom=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"))
            + "&dateTo=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));

        var byCategoryB = await clientB.GetFromJsonAsync<List<JsonElement>>(
            $"/api/financial/transactions/by-category?kind=Expense&{range}");
        byCategoryB.Should().BeEmpty("o tenant B não tem transações");

        var csvB = await clientB.GetStringAsync($"/api/financial/transactions/export?{range}");
        csvB.Should().NotContain("Só do A");
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta Teste",
            type = 0,
            currency = "EUR",
            openingBalanceAmount = 0m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<Guid> CreateExpenseCategoryAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name,
            kind = 0,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task CreateTransactionAsync(HttpClient client, Guid accountId, Guid categoryId, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            amount,
            currency = (string?)null,
            description = "test",
            tags = (string[]?)null,
        });
        response.EnsureSuccessStatusCode();
    }

    private sealed record IdRow(Guid Id);
}
