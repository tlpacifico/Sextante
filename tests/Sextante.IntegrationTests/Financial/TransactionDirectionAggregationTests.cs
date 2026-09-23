using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §1.3 (ADR-014 D4) — totais, filtros, export e orçamentos
/// deixam de depender de categories.kind: leem kind/direction da própria
/// transação, e só transações Regular contam como receita/despesa.
/// </summary>
public sealed class TransactionDirectionAggregationTests : IClassFixture<IdentityIntegrationFixture>
{
    private const short Inflow = 0;
    private const short Outflow = 1;
    private const short Regular = 0;
    private const short Transfer = 1;
    private const short Adjustment = 2;

    private readonly IdentityIntegrationFixture _fixture;

    public TransactionDirectionAggregationTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    private static string Range()
        => "dateFrom=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"))
            + "&dateTo=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));

    [Fact]
    public async Task Summary_counts_uncategorized_regular_transactions_by_direction()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "agg-uncat");
        var accountId = await CreateAccountAsync(client);

        await InsertAsync(tenantId, accountId, null, 30m, Outflow, Regular);

        var summary = await client.GetFromJsonAsync<JsonElement>($"/api/financial/transactions/summary?{Range()}");
        summary.GetProperty("expense").GetProperty("amount").GetDecimal().Should().Be(30m);
        summary.GetProperty("income").GetProperty("amount").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task Recategorizing_expense_to_income_moves_amount_between_totals()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "agg-recat");
        var accountId = await CreateAccountAsync(client);
        var expense = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var income = await CreateCategoryAsync(client, "Reembolso", kind: 1);
        var txId = await CreateTransactionAsync(client, accountId, expense, 50m);

        var before = await client.GetFromJsonAsync<JsonElement>($"/api/financial/transactions/summary?{Range()}");
        before.GetProperty("expense").GetProperty("amount").GetDecimal().Should().Be(50m);

        var patch = await client.PatchAsJsonAsync(
            "/api/financial/transactions/recategorize",
            new { ids = new[] { txId }, categoryId = income });
        patch.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/financial/transactions/summary?{Range()}");
        after.GetProperty("income").GetProperty("amount").GetDecimal().Should().Be(50m);
        after.GetProperty("expense").GetProperty("amount").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task Category_filter_never_returns_uncategorized_rows()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "agg-catfilter");
        var accountId = await CreateAccountAsync(client);
        var category = await CreateCategoryAsync(client, "Supermercado", kind: 0);
        await CreateTransactionAsync(client, accountId, category, 12m);
        await InsertAsync(tenantId, accountId, null, 7m, Outflow, Regular);

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/api/financial/transactions?categoryIds={category}&{Range()}");

        list.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Kind_filter_Transfer_and_Adjustment_return_only_rows_of_that_kind()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "agg-kind");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var transferId = Guid.NewGuid();
        await InsertAsync(tenantId, accountA, null, 100m, Outflow, Transfer, transferId);
        await InsertAsync(tenantId, accountB, null, 100m, Inflow, Transfer, transferId);
        await InsertAsync(tenantId, accountA, null, 5m, Inflow, Adjustment);

        async Task<int> CountAsync(string kind)
            => (await client.GetFromJsonAsync<JsonElement>($"/api/financial/transactions?kind={kind}&{Range()}"))
                .GetProperty("items").GetArrayLength();

        (await CountAsync("Transfer")).Should().Be(2);
        (await CountAsync("Adjustment")).Should().Be(1);
        (await CountAsync("Expense")).Should().Be(0);
        (await CountAsync("Income")).Should().Be(0);

        var summary = await client.GetFromJsonAsync<JsonElement>($"/api/financial/transactions/summary?{Range()}");
        summary.GetProperty("income").GetProperty("amount").GetDecimal().Should().Be(0m);
        summary.GetProperty("expense").GetProperty("amount").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task Export_labels_each_kind()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "agg-export");
        var accountId = await CreateAccountAsync(client);
        var expense = await CreateCategoryAsync(client, "Supermercado", kind: 0);
        var income = await CreateCategoryAsync(client, "Salário", kind: 1);
        await CreateTransactionAsync(client, accountId, expense, 10m);
        await CreateTransactionAsync(client, accountId, income, 20m);
        var transferId = Guid.NewGuid();
        await InsertAsync(tenantId, accountId, null, 30m, Outflow, Transfer, transferId);
        await InsertAsync(tenantId, accountId, null, 40m, Inflow, Adjustment);
        await InsertAsync(tenantId, accountId, null, 50m, Outflow, Regular);

        var csv = await client.GetStringAsync($"/api/financial/transactions/export?{Range()}");
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

        lines.Should().HaveCount(5);
        lines.Should().Contain(l => l.Contains(";Supermercado;Despesa;"));
        lines.Should().Contain(l => l.Contains(";Salário;Receita;"));
        lines.Should().Contain(l => l.Contains(";;Transferência;"));
        lines.Should().Contain(l => l.Contains(";;Acerto;"));
        lines.Should().Contain(l => l.Contains(";;Despesa;") && l.Contains("50"));
    }

    [Fact]
    public async Task Reapply_never_touches_transfer_legs_and_direct_edits_return_400()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "agg-nonreg");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var transferId = Guid.NewGuid();
        var legAId = await InsertAsync(tenantId, accountA, null, 100m, Outflow, Transfer, transferId);
        await InsertAsync(tenantId, accountB, null, 100m, Inflow, Transfer, transferId);

        var reapply = await client.PostAsync(
            $"/api/financial/categorization-rules/reapply?{Range()}",
            content: null);
        reapply.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var (categoryId, kind) = await ReadTransferRowAsync(legAId);
        categoryId.Should().BeNull();
        kind.Should().Be(Transfer);

        var expenseCategory = await CreateCategoryAsync(client, "Diversos", kind: 0);

        var putResponse = await client.PutAsJsonAsync(
            $"/api/financial/transactions/{legAId}",
            new
            {
                accountId = accountA,
                categoryId = expenseCategory,
                occurredAt = DateTimeOffset.UtcNow.AddHours(-1),
                amount = 100m,
                description = "tentativa de editar perna de transferência",
                tags = (string[]?)null,
            });
        putResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);

        var patchResponse = await client.PatchAsJsonAsync(
            "/api/financial/transactions/recategorize",
            new { ids = new[] { legAId }, categoryId = expenseCategory });
        patchResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    private async Task<(Guid? CategoryId, short Kind)> ReadTransferRowAsync(Guid id)
    {
        await using var conn = _fixture.OpenSuperuserConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT category_id, kind FROM financial.transactions WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var categoryId = reader.IsDBNull(0) ? (Guid?)null : reader.GetGuid(0);
        var kind = reader.GetInt16(1);
        return (categoryId, kind);
    }

    [Fact]
    public async Task Budget_progress_ignores_non_regular_rows()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "agg-budget");
        var accountId = await CreateAccountAsync(client);
        var category = await CreateCategoryAsync(client, "Supermercado", kind: 0);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var budget = await client.PostAsJsonAsync("/api/financial/budgets", new
        {
            categoryId = category,
            year = today.Year,
            month = today.Month,
            limitAmount = 100m,
            limitCurrency = "EUR",
            alertThresholdPercent = 80,
            notes = (string?)null,
        });
        budget.EnsureSuccessStatusCode();

        await CreateTransactionAsync(client, accountId, category, 40m);
        await InsertAsync(tenantId, accountId, null, 500m, Outflow, Adjustment);

        var budgets = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/financial/budgets?year={today.Year}&month={today.Month}");
        budgets.Should().ContainSingle()
            .Which.GetProperty("progress").GetProperty("spentAmount").GetDecimal().Should().Be(40m);
    }

    private async Task<Guid> InsertAsync(
        Guid tenantId, Guid accountId, Guid? categoryId, decimal amount,
        short direction, short kind, Guid? transferId = null)
    {
        var id = Guid.NewGuid();
        await using var conn = _fixture.OpenSuperuserConnection();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO financial.transactions " +
            "(id, tenant_id, account_id, category_id, occurred_at, amount, currency, tags, created_at, updated_at, version, direction, kind, transfer_id) " +
            "VALUES (@id, @t, @a, @c, now() - interval '1 hour', @amt, 'EUR', '[]'::jsonb, now(), now(), 1, @d, @k, @tr)",
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("a", accountId);
        cmd.Parameters.AddWithValue("c", (object?)categoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("amt", amount);
        cmd.Parameters.AddWithValue("d", direction);
        cmd.Parameters.AddWithValue("k", kind);
        cmd.Parameters.AddWithValue("tr", (object?)transferId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static Task<Guid> CreateAccountAsync(HttpClient client)
        => CreateAsync(client, "/api/financial/accounts", new
        {
            name = $"Conta {Guid.NewGuid():N}",
            type = 0,
            currency = "EUR",
            openingBalanceAmount = 0m,
        });

    private static Task<Guid> CreateCategoryAsync(HttpClient client, string name, int kind)
        => CreateAsync(client, "/api/financial/categories", new
        {
            name,
            kind,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });

    private static Task<Guid> CreateTransactionAsync(HttpClient client, Guid accountId, Guid categoryId, decimal amount)
        => CreateAsync(client, "/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt = DateTimeOffset.UtcNow.AddHours(-1),
            amount,
            currency = (string?)null,
            description = "test",
            tags = (string[]?)null,
        });

    private static async Task<Guid> CreateAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private sealed record IdRow(Guid Id);
}
