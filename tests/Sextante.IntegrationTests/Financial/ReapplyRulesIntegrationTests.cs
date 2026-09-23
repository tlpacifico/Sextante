using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §0.3 — reaplicar regras pedia PageSize=10000 a um repositório
/// que limita cada página a 100 linhas: só as 100 transações mais recentes
/// eram recategorizadas. Este teste passa pelo repositório real (cursor
/// sobre Postgres), não por um stub.
/// </summary>
public sealed class ReapplyRulesIntegrationTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public ReapplyRulesIntegrationTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Reapply_recategorizes_all_250_transactions_through_the_real_cursor()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "reapply-250");
        var accountId = await CreateAsync(client, "/api/financial/accounts", new
        {
            name = "Conta",
            type = 0,
            currency = "EUR",
            openingBalanceAmount = 0m,
        });
        var oldCategory = await CreateCategoryAsync(client, "Por classificar");
        var newCategory = await CreateCategoryAsync(client, "Supermercado");

        // Datas distintas e algumas repetidas, para exercitar o desempate
        // por Id no cursor (OccurredAt, Id).
        var baseDate = DateTimeOffset.UtcNow.AddDays(-200);
        for (var batch = 0; batch < 10; batch++)
        {
            await Task.WhenAll(Enumerable.Range(batch * 25, 25).Select(i =>
                client.PostAsJsonAsync("/api/financial/transactions", new
                {
                    accountId,
                    categoryId = oldCategory,
                    occurredAt = baseDate.AddDays(i / 2),
                    amount = 1m + i,
                    currency = (string?)null,
                    description = $"COMPRA CONTINENTE {i}",
                    tags = (string[]?)null,
                })));
        }

        var rule = await client.PostAsJsonAsync("/api/financial/categorization-rules", new
        {
            name = "Continente",
            pattern = "CONTINENTE",
            matchType = "Contains",
            categoryId = newCategory,
            priority = 1,
        });
        rule.EnsureSuccessStatusCode();

        var reapply = await client.PostAsync(
            $"/api/financial/categorization-rules/reapply?categoryId={oldCategory}&onlyUncategorized=false",
            content: null);
        reapply.EnsureSuccessStatusCode();
        var result = await reapply.Content.ReadFromJsonAsync<JsonElement>();

        result.GetProperty("totalProcessed").GetInt32().Should().Be(250);
        result.GetProperty("categorizedCount").GetInt32().Should().Be(250);

        var remaining = await client.GetFromJsonAsync<JsonElement>(
            $"/api/financial/transactions?categoryIds={oldCategory}&pageSize=100");
        remaining.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, string name)
        => await CreateAsync(client, "/api/financial/categories", new
        {
            name,
            kind = 0,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });

    private static async Task<Guid> CreateAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private sealed record IdRow(Guid Id);
}
