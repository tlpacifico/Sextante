using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §4 (grupo 4) — <c>POST /api/financial/accounts/{id}/reconcile</c>:
/// calcula o saldo à data, cria um único <c>Adjustment</c> pela diferença
/// (entrada se o real é maior, saída se menor), e o saldo à data passa a
/// coincidir com o real indicado. Acertos contam no saldo, não nos totais.
/// </summary>
public sealed class AccountReconciliationTests : IClassFixture<IdentityIntegrationFixture>
{
    private const short Checking = 0;
    private const short CreditCard = 3;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly IdentityIntegrationFixture _fixture;

    public AccountReconciliationTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Positive_difference_creates_inflow_adjustment_and_balance_matches()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-pos");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m, openingBalanceDate: Today.AddDays(-10));
        var category = await CreateCategoryAsync(client, "Diversos", kind: 0);
        await CreateTransactionAsync(client, accountId, category, 20m, DateTimeOffset.UtcNow.AddDays(-2));

        var response = await ReconcileAsync(client, accountId, Today, 95m);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ReconcileRow>())!;
        body.CalculatedBalance.Amount.Should().Be(80m);
        body.ActualBalance.Amount.Should().Be(95m);
        body.Difference.Amount.Should().Be(15m);
        body.Adjustment.Should().NotBeNull();
        body.Adjustment!.Kind.Should().Be("Adjustment");
        body.Adjustment.Direction.Should().Be("Inflow");
        body.Adjustment.Amount.Amount.Should().Be(15m);
        body.Adjustment.CategoryId.Should().BeNull();
        body.Adjustment.Description.Should().Be("Acerto de saldo");

        (await GetCurrentBalanceAsync(client, accountId)).Should().Be(95m);
    }

    [Fact]
    public async Task Negative_difference_creates_outflow_adjustment()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-neg");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m);

        var body = await ReconcileOkAsync(client, accountId, Today, 70m);

        body.Difference.Amount.Should().Be(-30m);
        body.Adjustment!.Direction.Should().Be("Outflow");
        body.Adjustment.Amount.Amount.Should().Be(30m);
        (await GetCurrentBalanceAsync(client, accountId)).Should().Be(70m);
    }

    [Fact]
    public async Task Zero_difference_creates_nothing()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-zero");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m);

        var body = await ReconcileOkAsync(client, accountId, Today, 100m);

        body.Difference.Amount.Should().Be(0m);
        body.Adjustment.Should().BeNull();
        (await CountTransactionsAsync(client, accountId)).Should().Be(0);
    }

    [Fact]
    public async Task Past_date_balance_at_date_equals_actual_even_with_later_transactions()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-past");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m, openingBalanceDate: Today.AddDays(-10));
        var category = await CreateCategoryAsync(client, "Diversos", kind: 0);
        await CreateTransactionAsync(client, accountId, category, 20m, DateTimeOffset.UtcNow.AddDays(-5));
        await CreateTransactionAsync(client, accountId, category, 10m, DateTimeOffset.UtcNow.AddDays(-1));

        var date = Today.AddDays(-3);
        var body = await ReconcileOkAsync(client, accountId, date, 90m);

        body.CalculatedBalance.Amount.Should().Be(80m);
        body.Adjustment!.Direction.Should().Be("Inflow");
        body.Adjustment.Amount.Amount.Should().Be(10m);

        var atDate = await client.GetFromJsonAsync<BalanceRow>(
            $"/api/financial/accounts/{accountId}/balance?at={date:yyyy-MM-dd}");
        atDate!.Balance.Amount.Should().Be(90m);
        (await GetCurrentBalanceAsync(client, accountId)).Should().Be(80m);
    }

    [Fact]
    public async Task Credit_card_debt_signs()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-card");
        var cardId = await CreateAccountAsync(
            client, type: CreditCard, openingBalanceAmount: -500m, openingBalanceDate: Today.AddDays(-10));

        var more = await ReconcileOkAsync(client, cardId, Today, -650m);
        more.Adjustment!.Direction.Should().Be("Outflow");
        more.Adjustment.Amount.Amount.Should().Be(150m);

        var less = await ReconcileOkAsync(client, cardId, Today, -400m);
        less.CalculatedBalance.Amount.Should().Be(-650m);
        less.Adjustment!.Direction.Should().Be("Inflow");
        less.Adjustment.Amount.Amount.Should().Be(250m);

        (await GetCurrentBalanceAsync(client, cardId)).Should().Be(-400m);
    }

    [Fact]
    public async Task Date_equal_to_opening_balance_date_is_accepted_and_day_before_is_rejected()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-obd");
        var openingBalanceDate = Today.AddDays(-10);
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m, openingBalanceDate: openingBalanceDate);

        var before = await ReconcileAsync(client, accountId, openingBalanceDate.AddDays(-1), 120m);
        before.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await before.Content.ReadAsStringAsync()).Should().Contain("saldo inicial");

        var onDate = await ReconcileAsync(client, accountId, openingBalanceDate, 120m);
        onDate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetCurrentBalanceAsync(client, accountId)).Should().Be(120m);
    }

    [Fact]
    public async Task Past_date_adjustment_is_placed_at_utc_noon_so_it_shows_on_the_same_local_day()
    {
        // Revisão final do grupo 4: às 23:59:59Z o acerto aparecia no dia
        // seguinte em UTC+1 (Lisboa no verão).
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-noon");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m, openingBalanceDate: Today.AddDays(-10));

        var date = Today.AddDays(-3);
        var body = await ReconcileOkAsync(client, accountId, date, 120m);

        body.Adjustment!.OccurredAt.Should().Be(
            new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Tomorrow_in_utc_is_accepted_for_timezones_ahead_of_utc()
    {
        // Revisão final do grupo 4: entre as 00:00 e a 01:00 em UTC+1 o "hoje"
        // local ainda é "amanhã" em UTC — o dialog pré-preenche-o.
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-tomorrow");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m);

        var body = await ReconcileOkAsync(client, accountId, Today.AddDays(1), 90m);

        body.Adjustment!.OccurredAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        (await GetCurrentBalanceAsync(client, accountId)).Should().Be(90m);
    }

    [Fact]
    public async Task Future_date_is_rejected()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-future");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m);

        var response = await ReconcileAsync(client, accountId, Today.AddDays(2), 50m);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountTransactionsAsync(client, accountId)).Should().Be(0);
    }

    [Fact]
    public async Task Adjustment_does_not_change_summary_totals()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-summary");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m, openingBalanceDate: Today.AddDays(-10));
        var category = await CreateCategoryAsync(client, "Diversos", kind: 0);
        await CreateTransactionAsync(client, accountId, category, 20m, DateTimeOffset.UtcNow.AddHours(-1));

        var range = "dateFrom=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))
            + "&dateTo=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));
        var before = await client.GetFromJsonAsync<JsonElement>($"/api/financial/transactions/summary?{range}");

        await ReconcileOkAsync(client, accountId, Today, 30m);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/financial/transactions/summary?{range}");
        after.GetProperty("income").GetProperty("amount").GetDecimal()
            .Should().Be(before.GetProperty("income").GetProperty("amount").GetDecimal());
        after.GetProperty("expense").GetProperty("amount").GetDecimal()
            .Should().Be(before.GetProperty("expense").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Adjustment_can_be_deleted_but_not_edited()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-delete");
        var accountId = await CreateAccountAsync(client, openingBalanceAmount: 100m);
        var category = await CreateCategoryAsync(client, "Diversos", kind: 0);

        var body = await ReconcileOkAsync(client, accountId, Today, 60m);
        var adjustmentId = body.Adjustment!.Id;

        var put = await client.PutAsJsonAsync($"/api/financial/transactions/{adjustmentId}", new
        {
            accountId,
            categoryId = category,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            amount = 40m,
            description = "editado",
            tags = (string[]?)null,
        });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var delete = await client.DeleteAsync($"/api/financial/transactions/{adjustmentId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetCurrentBalanceAsync(client, accountId)).Should().Be(100m);
    }

    [Fact]
    public async Task Unknown_account_returns_404()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rec-404");

        var response = await ReconcileAsync(client, Guid.NewGuid(), Today, 10m);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> ReconcileAsync(HttpClient client, Guid accountId, DateOnly date, decimal actualBalance)
        => client.PostAsJsonAsync($"/api/financial/accounts/{accountId}/reconcile", new
        {
            date = date.ToString("yyyy-MM-dd"),
            actualBalance,
        });

    private static async Task<ReconcileRow> ReconcileOkAsync(HttpClient client, Guid accountId, DateOnly date, decimal actualBalance)
    {
        var response = await ReconcileAsync(client, accountId, date, actualBalance);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ReconcileRow>())!;
    }

    private static async Task<decimal> GetCurrentBalanceAsync(HttpClient client, Guid accountId)
    {
        var account = await client.GetFromJsonAsync<AccountRow>($"/api/financial/accounts/{accountId}");
        return account!.CurrentBalance.Amount;
    }

    private static async Task<int> CountTransactionsAsync(HttpClient client, Guid accountId)
    {
        var page = await client.GetFromJsonAsync<TxPage>(
            $"/api/financial/transactions?accountIds={accountId}&pageSize=100");
        return page!.Items.Count;
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

    private static Task<Guid> CreateTransactionAsync(
        HttpClient client, Guid accountId, Guid categoryId, decimal amount, DateTimeOffset occurredAt)
        => CreateAsync(client, "/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt,
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

    private sealed record MoneyValue(decimal Amount, string Currency);

    private sealed record AccountRow(Guid Id, MoneyValue CurrentBalance);

    private sealed record BalanceRow(Guid AccountId, DateOnly? At, MoneyValue Balance);

    private sealed record TransactionRow(
        Guid Id,
        Guid AccountId,
        Guid? CategoryId,
        DateTimeOffset OccurredAt,
        MoneyValue Amount,
        string? Description,
        string Direction,
        string Kind);

    private sealed record ReconcileRow(
        Guid AccountId,
        DateOnly Date,
        MoneyValue CalculatedBalance,
        MoneyValue ActualBalance,
        MoneyValue Difference,
        TransactionRow? Adjustment);

    private sealed record TxItem(Guid Id);

    private sealed record TxPage(IReadOnlyList<TxItem> Items, string? NextCursor);
}
