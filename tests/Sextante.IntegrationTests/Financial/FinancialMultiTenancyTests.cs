using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Tech-stack §4.7 — testes obrigatórios de multi-tenancy aplicados ao
/// módulo Financial.
/// </summary>
public sealed class FinancialMultiTenancyTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public FinancialMultiTenancyTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_A_cannot_read_tenant_B_accounts()
    {
        var (clientA, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-acc");
        var (clientB, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-acc");
        tenantA.Should().NotBe(tenantB);

        var createA = await clientA.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta A",
            type = 0,
            openingBalanceAmount = 100m,
        });
        createA.EnsureSuccessStatusCode();
        var accountA = await createA.Content.ReadFromJsonAsync<IdRow>();

        // Tenant B lista contas: não vê a do tenant A.
        var listB = await clientB.GetFromJsonAsync<List<IdRow>>("/api/financial/accounts");
        listB.Should().NotBeNull();
        listB!.Should().NotContain(a => a.Id == accountA!.Id);

        // Tenant B tenta ler/atualizar/arquivar a conta de A — devolve 404,
        // não 403 (anti-enumeração).
        var getResponse = await clientB.GetAsync($"/api/financial/accounts/{accountA!.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var putResponse = await clientB.PutAsJsonAsync($"/api/financial/accounts/{accountA.Id}", new
        {
            name = "Hijack",
            type = 0,
        });
        putResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var deleteResponse = await clientB.DeleteAsync($"/api/financial/accounts/{accountA.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unauthenticated_request_to_financial_returns_401()
    {
        var anonymous = _fixture.Factory.CreateClient();
        var response = await anonymous.GetAsync("/api/financial/accounts");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tenant_A_cannot_read_tenant_B_budgets()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-bdg");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-bdg");

        var year = DateOnly.FromDateTime(DateTime.UtcNow).Year;
        var month = DateOnly.FromDateTime(DateTime.UtcNow).Month;

        // Tenant A cria categoria + budget.
        var categoryA = await CreateCategoryAsync(clientA, "Habitação");
        var budgetA = await CreateBudgetAsync(clientA, categoryA, year, month);

        // Tenant B lista budgets do mesmo período: vazio.
        var listB = await clientB.GetFromJsonAsync<List<BudgetIdRow>>(
            $"/api/financial/budgets?year={year}&month={month}");
        listB.Should().NotBeNull();
        listB!.Should().NotContain(b => b.Id == budgetA);

        // Tenant B tenta GET / PUT / DELETE no budget de A → 404.
        var get = await clientB.GetAsync($"/api/financial/budgets/{budgetA}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var put = await clientB.PutAsJsonAsync($"/api/financial/budgets/{budgetA}", new
        {
            limitAmount = 999m,
            limitCurrency = "EUR",
            alertThresholdPercent = 90,
            notes = "hijack",
        });
        put.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var del = await clientB.DeleteAsync($"/api/financial/budgets/{budgetA}");
        del.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var progress = await clientB.GetAsync($"/api/financial/budgets/{budgetA}/progress");
        progress.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tenant_A_cannot_acknowledge_tenant_B_alerts()
    {
        // Set-up minimal: B tem o seu próprio espaço, sem alerts.
        // A tenta acknowledge de uma id arbitrária — deve dar 404
        // (mesmo comportamento que se fosse um alert do B).
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-alert");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-alert");

        var listA = await clientA.GetFromJsonAsync<List<BudgetIdRow>>(
            "/api/financial/budgets/alerts/active");
        listA.Should().BeEmpty();

        var listB = await clientB.GetFromJsonAsync<List<BudgetIdRow>>(
            "/api/financial/budgets/alerts/active");
        listB.Should().BeEmpty();

        var ackA = await clientA.PostAsync(
            $"/api/financial/budgets/alerts/{Guid.NewGuid()}/acknowledge",
            content: null);
        ackA.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, string name)
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

    private static async Task<Guid> CreateBudgetAsync(HttpClient client, Guid categoryId, int year, int month)
    {
        var response = await client.PostAsJsonAsync("/api/financial/budgets", new
        {
            categoryId,
            year,
            month,
            limitAmount = 500m,
            limitCurrency = "EUR",
            alertThresholdPercent = 80,
            notes = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetIdRow>())!.Id;
    }

    private sealed record IdRow(Guid Id);
    private sealed record BudgetIdRow(Guid Id);
}
