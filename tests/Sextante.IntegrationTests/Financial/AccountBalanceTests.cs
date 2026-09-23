using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §2.3 (grupo 2, task 3) — <c>IAccountBalanceQuery</c> soma
/// <c>OpeningBalance</c> com todas as transações desde
/// <c>OpeningBalanceDate</c>, por todos os tipos (Regular, Transfer,
/// Adjustment) — diferente dos totais de receita/despesa, que só contam
/// <c>Regular</c>.
/// </summary>
public sealed class AccountBalanceTests : IClassFixture<IdentityIntegrationFixture>
{
    private const short Inflow = 0;
    private const short Outflow = 1;
    private const short Regular = 0;
    private const short Transfer = 1;
    private const short Adjustment = 2;

    private const short Checking = 0;
    private const short CreditCard = 3;

    private readonly IdentityIntegrationFixture _fixture;

    public AccountBalanceTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Current_balance_includes_all_transaction_kinds()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bal-all-kinds");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m);
        var category = await CreateCategoryAsync(client, "Diversos", kind: 0);

        // Regular Outflow 20 via API.
        await CreateTransactionAsync(client, accountId, category, 20m);

        // Transfer leg Inflow 30 e Adjustment Outflow 5 via superuser — sem
        // endpoint público ainda (grupo posterior).
        var transferId = Guid.NewGuid();
        await InsertAsync(tenantId, accountId, null, 30m, Inflow, Transfer, DateTimeOffset.UtcNow.AddHours(-1), transferId);
        await InsertAsync(tenantId, accountId, null, 5m, Outflow, Adjustment, DateTimeOffset.UtcNow.AddHours(-1));

        // 100 - 20 + 30 - 5 = 105.
        var list = await client.GetFromJsonAsync<List<AccountRow>>("/api/financial/accounts");
        list.Should().ContainSingle(a => a.Id == accountId)
            .Which.CurrentBalance.Amount.Should().Be(105m);

        var detail = await client.GetFromJsonAsync<AccountRow>($"/api/financial/accounts/{accountId}");
        detail!.CurrentBalance.Amount.Should().Be(105m);
    }

    [Fact]
    public async Task Transactions_before_opening_balance_date_are_excluded()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bal-obd-cutoff");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accountId = await CreateAccountAsync(
            client, openingBalanceAmount: 100m, openingBalanceDate: today.AddDays(-10));

        // Antes da data do saldo inicial — não entra.
        await InsertAsync(
            tenantId, accountId, null, 999m, Outflow, Adjustment,
            DateTimeOffset.UtcNow.AddDays(-20));

        // Depois da data do saldo inicial — entra.
        await InsertAsync(
            tenantId, accountId, null, 20m, Inflow, Adjustment,
            DateTimeOffset.UtcNow.AddDays(-5));

        var detail = await client.GetFromJsonAsync<AccountRow>($"/api/financial/accounts/{accountId}");
        detail!.CurrentBalance.Amount.Should().Be(120m);
    }

    [Fact]
    public async Task Balance_at_date_cuts_inclusive_by_day()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bal-as-of");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var accountId = await CreateAccountAsync(
            client, openingBalanceAmount: 0m, openingBalanceDate: today.AddDays(-10));

        var d1 = today.AddDays(-3);
        var d2 = today.AddDays(-2);
        await InsertAsync(
            tenantId, accountId, null, 50m, Inflow, Adjustment,
            new DateTimeOffset(d1.Year, d1.Month, d1.Day, 10, 0, 0, TimeSpan.Zero));
        await InsertAsync(
            tenantId, accountId, null, 30m, Inflow, Adjustment,
            new DateTimeOffset(d2.Year, d2.Month, d2.Day, 10, 0, 0, TimeSpan.Zero));

        var balance = await client.GetFromJsonAsync<BalanceRow>(
            $"/api/financial/accounts/{accountId}/balance?at={d1:yyyy-MM-dd}");
        balance!.Balance.Amount.Should().Be(50m);
    }

    [Fact]
    public async Task Credit_card_can_have_negative_current_balance()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bal-cc-negative");
        var accountId = await CreateAccountAsync(client, type: CreditCard, openingBalanceAmount: -500m);

        var list = await client.GetFromJsonAsync<List<AccountRow>>("/api/financial/accounts");
        list.Should().ContainSingle(a => a.Id == accountId)
            .Which.CurrentBalance.Amount.Should().Be(-500m);

        var detail = await client.GetFromJsonAsync<AccountRow>($"/api/financial/accounts/{accountId}");
        detail!.CurrentBalance.Amount.Should().Be(-500m);
    }

    [Fact]
    public async Task Balance_of_another_tenants_account_returns_404()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bal-tenant-a");
        var accountId = await CreateAccountAsync(clientA, openingBalanceAmount: 10m);

        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bal-tenant-b");
        var response = await clientB.GetAsync($"/api/financial/accounts/{accountId}/balance");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<Guid> CreateAccountAsync(
        HttpClient client,
        short type = Checking,
        decimal openingBalanceAmount = 0m,
        DateOnly? openingBalanceDate = null)
        => CreateAsync(client, "/api/financial/accounts", new
        {
            name = $"Conta {Guid.NewGuid():N}",
            type,
            currency = "EUR",
            openingBalanceAmount,
            openingBalanceDate,
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

    private async Task<Guid> InsertAsync(
        Guid tenantId, Guid accountId, Guid? categoryId, decimal amount,
        short direction, short kind, DateTimeOffset occurredAt, Guid? transferId = null)
    {
        var id = Guid.NewGuid();
        await using var conn = _fixture.OpenSuperuserConnection();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO financial.transactions " +
            "(id, tenant_id, account_id, category_id, occurred_at, amount, currency, tags, created_at, updated_at, version, direction, kind, transfer_id) " +
            "VALUES (@id, @t, @a, @c, @occ, @amt, 'EUR', '[]'::jsonb, now(), now(), 1, @d, @k, @tr)",
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("a", accountId);
        cmd.Parameters.AddWithValue("c", (object?)categoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("occ", occurredAt);
        cmd.Parameters.AddWithValue("amt", amount);
        cmd.Parameters.AddWithValue("d", direction);
        cmd.Parameters.AddWithValue("k", kind);
        cmd.Parameters.AddWithValue("tr", (object?)transferId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private sealed record IdRow(Guid Id);

    private sealed record MoneyValue(decimal Amount, string Currency);

    private sealed record AccountRow(Guid Id, string Name, string Type, MoneyValue CurrentBalance);

    private sealed record BalanceRow(Guid AccountId, DateOnly? At, MoneyValue Balance);
}
