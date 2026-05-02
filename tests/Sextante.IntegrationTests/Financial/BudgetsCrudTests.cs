using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

public sealed class BudgetsCrudTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public BudgetsCrudTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Full_crud_lifecycle_for_budget()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bdg");
        var categoryId = await CreateExpenseCategoryAsync(client, "Habitação");

        var year = DateOnly.FromDateTime(DateTime.UtcNow).Year;
        var month = DateOnly.FromDateTime(DateTime.UtcNow).Month;

        // List inicial: vazio.
        var initial = await client.GetFromJsonAsync<List<BudgetRow>>(
            $"/api/financial/budgets?year={year}&month={month}");
        initial.Should().NotBeNull().And.BeEmpty();

        // Create.
        var createResponse = await client.PostAsJsonAsync("/api/financial/budgets", new
        {
            categoryId,
            year,
            month,
            limitAmount = 500m,
            limitCurrency = "EUR",
            alertThresholdPercent = 80,
            notes = "Renda + condomínio",
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<BudgetRow>();
        created!.LimitAmount.Should().Be(500m);
        created.LimitCurrency.Should().Be("EUR");
        created.AlertThresholdPercent.Should().Be(80);

        // Get by id.
        var getResp = await client.GetAsync($"/api/financial/budgets/{created.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Update — sobe o limit.
        var updateResp = await client.PutAsJsonAsync($"/api/financial/budgets/{created.Id}", new
        {
            limitAmount = 700m,
            limitCurrency = "EUR",
            alertThresholdPercent = 90,
            notes = "atualizado",
        });
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<BudgetRow>();
        updated!.LimitAmount.Should().Be(700m);
        updated.AlertThresholdPercent.Should().Be(90);

        // Progress endpoint — sem transactions ainda → 0.
        var progress = await client.GetFromJsonAsync<ProgressRow>(
            $"/api/financial/budgets/{created.Id}/progress");
        progress!.SpentAmount.Should().Be(0m);
        progress.PercentUsed.Should().Be(0m);
        progress.HasIncompleteRates.Should().BeFalse();

        // Delete (soft).
        var deleteResp = await client.DeleteAsync($"/api/financial/budgets/{created.Id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // List depois de archive: vazio.
        var afterDelete = await client.GetFromJsonAsync<List<BudgetRow>>(
            $"/api/financial/budgets?year={year}&month={month}");
        afterDelete.Should().BeEmpty();

        var getAfter = await client.GetAsync($"/api/financial/budgets/{created.Id}");
        getAfter.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_for_income_category_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bdg-inc");
        var incomeCategoryId = await CreateCategoryAsync(client, kind: 1, name: "Salário");

        var year = DateOnly.FromDateTime(DateTime.UtcNow).Year;
        var month = DateOnly.FromDateTime(DateTime.UtcNow).Month;

        var response = await client.PostAsJsonAsync("/api/financial/budgets", new
        {
            categoryId = incomeCategoryId,
            year,
            month,
            limitAmount = 500m,
            limitCurrency = "EUR",
            alertThresholdPercent = 80,
            notes = (string?)null,
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_duplicate_category_period_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bdg-dup");
        var categoryId = await CreateExpenseCategoryAsync(client, "Habitação");
        var year = DateOnly.FromDateTime(DateTime.UtcNow).Year;
        var month = DateOnly.FromDateTime(DateTime.UtcNow).Month;

        var first = await client.PostAsJsonAsync("/api/financial/budgets", new
        {
            categoryId, year, month,
            limitAmount = 500m, limitCurrency = "EUR",
            alertThresholdPercent = 80, notes = (string?)null,
        });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/financial/budgets", new
        {
            categoryId, year, month,
            limitAmount = 700m, limitCurrency = "EUR",
            alertThresholdPercent = 80, notes = (string?)null,
        });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ActiveAlerts_endpoint_returns_empty_when_no_alerts()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bdg-act");
        var alerts = await client.GetFromJsonAsync<List<AlertRow>>(
            "/api/financial/budgets/alerts/active");
        alerts.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Acknowledge_nonexistent_alert_returns_404()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bdg-ack");
        var response = await client.PostAsync(
            $"/api/financial/budgets/alerts/{Guid.NewGuid()}/acknowledge", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_default_uses_current_month()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bdg-cur");
        var resp = await client.GetAsync("/api/financial/budgets");
        resp.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateExpenseCategoryAsync(HttpClient client, string name)
        => await CreateCategoryAsync(client, kind: 0, name: name);

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, int kind, string name)
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

    private sealed record IdRow(Guid Id);
    private sealed record BudgetRow(
        Guid Id,
        Guid CategoryId,
        int Year,
        int Month,
        decimal LimitAmount,
        string LimitCurrency,
        int AlertThresholdPercent,
        string? Notes);
    private sealed record ProgressRow(
        decimal LimitAmount,
        string LimitCurrency,
        decimal SpentAmount,
        decimal RemainingAmount,
        decimal PercentUsed,
        decimal? ProjectedAmount,
        bool HasIncompleteRates);
    private sealed record AlertRow(Guid Id, Guid BudgetId, int Threshold, bool Acknowledged);
}
