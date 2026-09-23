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

    [Fact]
    public async Task Reconcile_account_of_other_tenant_returns_404_and_creates_nothing()
    {
        // Phase 6.5 grupo 4 — reconciliar a conta de outro tenant: 404 e
        // nenhum acerto criado.
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-rec");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-rec");

        var accountResponse = await clientA.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta A",
            type = 0,
            currency = "EUR",
            openingBalanceAmount = 100m,
        });
        accountResponse.EnsureSuccessStatusCode();
        var accountA = (await accountResponse.Content.ReadFromJsonAsync<IdRow>())!.Id;

        var reconcile = await clientB.PostAsJsonAsync($"/api/financial/accounts/{accountA}/reconcile", new
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            actualBalance = 5m,
        });
        reconcile.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var pageA = await clientA.GetFromJsonAsync<TxPage>($"/api/financial/transactions?accountIds={accountA}");
        pageA!.Items.Should().BeEmpty();
        var pageB = await clientB.GetFromJsonAsync<TxPage>("/api/financial/transactions");
        pageB!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Credit_card_view_of_other_tenant_returns_404()
    {
        // Phase 6.5 grupo 5 — a vista do cartão respeita o isolamento.
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-cc");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-cc");
        var cardA = await CreateAccountAsync(clientA, type: 3);

        var response = await clientB.GetAsync($"/api/financial/accounts/{cardA}/credit-card");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Payment_account_of_other_tenant_is_rejected()
    {
        // Phase 6.5 grupo 5 — a conta de pagamento tem de ser do próprio tenant.
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-ccpay");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-ccpay");
        var checkingA = await CreateAccountAsync(clientA, type: 0);
        var cardB = await CreateAccountAsync(clientB, type: 3);

        var put = await clientB.PutAsJsonAsync($"/api/financial/accounts/{cardB}", new
        {
            name = "Cartão B",
            type = 3,
            creditCard = new { creditLimit = 1000m, statementClosingDay = 20, paymentDueDay = 10, paymentAccountId = checkingA },
        });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var accountB = await clientB.GetFromJsonAsync<CardRow>($"/api/financial/accounts/{cardB}");
        accountB!.CreditCard.Should().BeNull();
    }

    [Fact]
    public async Task Transfer_rule_target_must_belong_to_tenant()
    {
        // Phase 6.5 grupo 7 — uma regra de transferência não pode apontar
        // para uma conta de outro tenant (soft reference validada no handler).
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-rulexfer");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-rulexfer");
        var cardA = await CreateAccountAsync(clientA, type: 3);

        var response = await clientB.PostAsJsonAsync("/api/financial/categorization-rules", new
        {
            name = "Pagamento cartão",
            pattern = "PAGAMENTO CARTAO",
            matchType = "Contains",
            categoryId = (Guid?)null,
            priority = 5,
            action = "MarkAsTransfer",
            targetAccountId = cardA,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Installment_plans_are_isolated_between_tenants()
    {
        // Phase 6.5 grupo 6 — planos de prestações de outro tenant nunca são
        // visíveis nem alteráveis, e não se criam sobre cartões/compras alheios.
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-ip");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-ip");
        var cardA = await CreateAccountAsync(clientA, type: 3);
        var cardB = await CreateAccountAsync(clientB, type: 3);
        var categoryA = await CreateCategoryAsync(clientA, "Tecnologia");

        var purchase = await clientA.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId = cardA,
            categoryId = categoryA,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            amount = 600m,
            currency = (string?)null,
            description = "Compra",
            tags = (string[]?)null,
        });
        purchase.EnsureSuccessStatusCode();
        var purchaseA = (await purchase.Content.ReadFromJsonAsync<IdRow>())!.Id;

        object Body(Guid accountId, Guid? purchaseId) => new
        {
            accountId,
            purchaseTransactionId = purchaseId,
            purchaseDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            description = "Plano",
            totalAmount = 600m,
            installmentCount = 6,
            installmentsAlreadyPaid = 0,
            firstInstallmentDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            annualRate = (decimal?)null,
        };

        var created = await clientA.PostAsJsonAsync("/api/financial/installment-plans", Body(cardA, purchaseA));
        created.EnsureSuccessStatusCode();
        var planA = (await created.Content.ReadFromJsonAsync<IdRow>())!.Id;

        (await clientB.GetAsync($"/api/financial/installment-plans/{planA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clientB.PutAsJsonAsync($"/api/financial/installment-plans/{planA}", Body(cardA, null))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await clientB.DeleteAsync($"/api/financial/installment-plans/{planA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clientB.GetFromJsonAsync<List<IdRow>>("/api/financial/installment-plans")).Should().BeEmpty();

        (await clientB.PostAsJsonAsync("/api/financial/installment-plans", Body(cardA, null))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await clientB.PostAsJsonAsync("/api/financial/installment-plans", Body(cardB, purchaseA))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        (await clientA.GetAsync($"/api/financial/installment-plans/{planA}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, short type)
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = $"Conta {Guid.NewGuid():N}",
            type,
            currency = "EUR",
            openingBalanceAmount = 0m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
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
    private sealed record TxItem(Guid Id);
    private sealed record TxPage(IReadOnlyList<TxItem> Items, string? NextCursor);
    private sealed record CardRow(Guid Id, object? CreditCard);
}
