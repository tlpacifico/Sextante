using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §6.3 (grupo 6) — CRUD de planos de prestações em
/// <c>/api/financial/installment-plans</c>: calendário na resposta, compra
/// opcional (despesa do mesmo cartão, um plano ativo por compra), só em
/// cartões, soft delete.
/// </summary>
public sealed class InstallmentPlansTests : IClassFixture<IdentityIntegrationFixture>
{
    private const short Checking = 0;
    private const short CreditCard = 3;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly IdentityIntegrationFixture _fixture;

    public InstallmentPlansTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_manual_plan_returns_schedule()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-create");
        var cardId = await CreateAccountAsync(client, CreditCard);
        var first = Today.AddMonths(-2);

        var response = await PostPlanAsync(client, cardId, null, 600m, 6, 0, first);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        response.Headers.Location.Should().NotBeNull();
        var plan = (await response.Content.ReadFromJsonAsync<PlanRow>())!;
        plan.InstallmentAmount.Amount.Should().Be(100m);
        plan.Schedule.Should().HaveCount(6);
        plan.Schedule.Take(3).Should().OnlyContain(i => i.Paid);
        plan.Schedule.Skip(3).Should().OnlyContain(i => !i.Paid);
        plan.InstallmentsPaidOrDue.Should().Be(3);
        plan.RemainingAmount.Amount.Should().Be(300m);
        plan.NextInstallmentDate.Should().Be(first.AddMonths(3));
        plan.IsActive.Should().BeTrue();
        plan.TotalAmount.Currency.Should().Be("EUR");
    }

    [Fact]
    public async Task Create_from_purchase_transaction()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-purchase");
        var cardId = await CreateAccountAsync(client, CreditCard);
        var expense = await CreateCategoryAsync(client, "Tecnologia", kind: 0);
        var purchaseId = await CreateTransactionAsync(client, cardId, expense, 600m);

        var created = await PostPlanAsync(client, cardId, purchaseId, 600m, 6, 0, Today);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await created.Content.ReadFromJsonAsync<PlanRow>())!.PurchaseTransactionId.Should().Be(purchaseId);

        var duplicate = await PostPlanAsync(client, cardId, purchaseId, 600m, 6, 0, Today);
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Purchase_must_be_an_expense_of_the_same_card()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-purchase-bad");
        var cardId = await CreateAccountAsync(client, CreditCard);
        var otherCardId = await CreateAccountAsync(client, CreditCard);
        var expense = await CreateCategoryAsync(client, "Tecnologia", kind: 0);
        var income = await CreateCategoryAsync(client, "Reembolsos", kind: 1);

        var otherAccountPurchase = await CreateTransactionAsync(client, otherCardId, expense, 600m);
        (await PostPlanAsync(client, cardId, otherAccountPurchase, 600m, 6, 0, Today))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var refund = await CreateTransactionAsync(client, cardId, income, 600m);
        (await PostPlanAsync(client, cardId, refund, 600m, 6, 0, Today))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await PostPlanAsync(client, cardId, Guid.NewGuid(), 600m, 6, 0, Today))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Plan_requires_credit_card_account()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-checking");
        var checkingId = await CreateAccountAsync(client, Checking);

        (await PostPlanAsync(client, checkingId, null, 600m, 6, 0, Today))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await PostPlanAsync(client, Guid.NewGuid(), null, 600m, 6, 0, Today))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(600, 1, 0, "Plano")]
    [InlineData(600, 6, 6, "Plano")]
    [InlineData(0, 6, 0, "Plano")]
    [InlineData(600, 6, 0, "")]
    public async Task Validation_errors_return_400(double total, int count, int alreadyPaid, string description)
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-validation");
        var cardId = await CreateAccountAsync(client, CreditCard);

        var response = await PostPlanAsync(client, cardId, null, (decimal)total, count, alreadyPaid, Today, description);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_changes_fields_but_not_account()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-update");
        var cardId = await CreateAccountAsync(client, CreditCard);
        var plan = await CreatePlanAsync(client, cardId);

        var put = await client.PutAsJsonAsync($"/api/financial/installment-plans/{plan.Id}", new
        {
            purchaseTransactionId = (Guid?)null,
            purchaseDate = Today.AddMonths(-2).ToString("yyyy-MM-dd"),
            description = "Frigorífico",
            totalAmount = 1200m,
            installmentCount = 12,
            installmentsAlreadyPaid = 2,
            firstInstallmentDate = Today.AddMonths(-1).ToString("yyyy-MM-dd"),
            annualRate = 4.5m,
        });

        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var updated = (await put.Content.ReadFromJsonAsync<PlanRow>())!;
        updated.Description.Should().Be("Frigorífico");
        updated.InstallmentCount.Should().Be(12);
        updated.InstallmentsAlreadyPaid.Should().Be(2);
        updated.AnnualRate.Should().Be(4.5m);
        updated.AccountId.Should().Be(cardId);
    }

    [Fact]
    public async Task Delete_soft_deletes_and_frees_the_purchase()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-delete");
        var cardId = await CreateAccountAsync(client, CreditCard);
        var expense = await CreateCategoryAsync(client, "Tecnologia", kind: 0);
        var purchaseId = await CreateTransactionAsync(client, cardId, expense, 600m);
        var created = await PostPlanAsync(client, cardId, purchaseId, 600m, 6, 0, Today);
        var plan = (await created.Content.ReadFromJsonAsync<PlanRow>())!;

        (await client.DeleteAsync($"/api/financial/installment-plans/{plan.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/financial/installment-plans/{plan.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<List<PlanRow>>("/api/financial/installment-plans"))
            .Should().BeEmpty();
        (await client.DeleteAsync($"/api/financial/installment-plans/{plan.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        (await PostPlanAsync(client, cardId, purchaseId, 600m, 6, 0, Today)).StatusCode
            .Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Linked_plan_takes_purchase_date_from_the_transaction()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-purchase-date");
        var cardId = await CreateAccountAsync(client, CreditCard);
        var expense = await CreateCategoryAsync(client, "Tecnologia", kind: 0);
        var purchaseId = await CreateTransactionAsync(client, cardId, expense, 600m);

        // A data enviada é ignorada: num plano ligado vem da compra.
        var created = await PostPlanAsync(client, cardId, purchaseId, 600m, 6, 0, Today.AddMonths(5));

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await created.Content.ReadFromJsonAsync<PlanRow>())!.PurchaseDate
            .Should().Be(DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddMinutes(-5).UtcDateTime));
    }

    [Fact]
    public async Task Plan_with_deleted_purchase_stays_editable()
    {
        // Revisão profunda do grupo 6: a compra ligada foi apagada → o PUT com
        // a mesma ligação dava 404 e o plano ficava impossível de editar.
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-deleted-purchase");
        var cardId = await CreateAccountAsync(client, CreditCard);
        var expense = await CreateCategoryAsync(client, "Tecnologia", kind: 0);
        var purchaseId = await CreateTransactionAsync(client, cardId, expense, 600m);
        var created = await PostPlanAsync(client, cardId, purchaseId, 600m, 6, 0, Today);
        var plan = (await created.Content.ReadFromJsonAsync<PlanRow>())!;

        (await client.DeleteAsync($"/api/financial/transactions/{purchaseId}")).EnsureSuccessStatusCode();

        var put = await client.PutAsJsonAsync($"/api/financial/installment-plans/{plan.Id}", new
        {
            purchaseTransactionId = purchaseId,
            purchaseDate = plan.PurchaseDate.ToString("yyyy-MM-dd"),
            description = "Portátil (renomeado)",
            totalAmount = 600m,
            installmentCount = 6,
            installmentsAlreadyPaid = 0,
            firstInstallmentDate = plan.FirstInstallmentDate.ToString("yyyy-MM-dd"),
            annualRate = (decimal?)null,
        });

        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Purchase_in_other_currency_is_rejected()
    {
        // Revisão profunda do grupo 6: 100 USD num cartão EUR virava 100 EUR.
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-fx");
        var cardId = await CreateAccountAsync(client, CreditCard);
        var expense = await CreateCategoryAsync(client, "Viagens", kind: 0);
        await SeedExchangeRateAsync("EUR", "USD", 1.10m);
        var response = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId = cardId,
            categoryId = expense,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            amount = 100m,
            currency = "USD",
            description = "Hotel",
            tags = (string[]?)null,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var usdPurchase = (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;

        (await PostPlanAsync(client, cardId, usdPurchase, 100m, 2, 0, Today))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_filters_by_account()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-list");
        var cardA = await CreateAccountAsync(client, CreditCard);
        var cardB = await CreateAccountAsync(client, CreditCard);
        var planA = await CreatePlanAsync(client, cardA);
        await CreatePlanAsync(client, cardB);

        var all = await client.GetFromJsonAsync<List<PlanRow>>("/api/financial/installment-plans");
        all.Should().HaveCount(2);

        var onlyA = await client.GetFromJsonAsync<List<PlanRow>>($"/api/financial/installment-plans?accountId={cardA}");
        onlyA.Should().ContainSingle().Which.Id.Should().Be(planA.Id);
    }

    private static Task<HttpResponseMessage> PostPlanAsync(
        HttpClient client, Guid accountId, Guid? purchaseTransactionId, decimal totalAmount,
        int installmentCount, int installmentsAlreadyPaid, DateOnly firstInstallmentDate, string description = "Portátil")
        => client.PostAsJsonAsync("/api/financial/installment-plans", new
        {
            accountId,
            purchaseTransactionId,
            purchaseDate = firstInstallmentDate.AddMonths(-1).ToString("yyyy-MM-dd"),
            description,
            totalAmount,
            installmentCount,
            installmentsAlreadyPaid,
            firstInstallmentDate = firstInstallmentDate.ToString("yyyy-MM-dd"),
            annualRate = (decimal?)null,
        });

    private static async Task<PlanRow> CreatePlanAsync(HttpClient client, Guid cardId)
    {
        var response = await PostPlanAsync(client, cardId, null, 600m, 6, 0, Today);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PlanRow>())!;
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

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, string name, int kind)
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

    private static async Task<Guid> CreateTransactionAsync(HttpClient client, Guid accountId, Guid categoryId, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            amount,
            currency = (string?)null,
            description = "Compra",
            tags = (string[]?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private async Task SeedExchangeRateAsync(string from, string to, decimal rate)
    {
        await using var superConn = _fixture.OpenSuperuserConnection();
        await using var seedCmd = new NpgsqlCommand(
            """
            INSERT INTO shared."exchange_rates"
              ("id", "rate_date", "from_currency", "to_currency", "rate", "source",
               "created_at", "updated_at", "version")
            VALUES
              (@id, @rateDate, @from, @to, @rate, 'manual',
               now(), now(), 1)
            ON CONFLICT ("rate_date", "from_currency", "to_currency") DO NOTHING
            """,
            superConn);
        seedCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        seedCmd.Parameters.AddWithValue("rateDate", DateOnly.FromDateTime(DateTime.UtcNow));
        seedCmd.Parameters.AddWithValue("from", from);
        seedCmd.Parameters.AddWithValue("to", to);
        seedCmd.Parameters.AddWithValue("rate", rate);
        await seedCmd.ExecuteNonQueryAsync();
    }

    private sealed record IdRow(Guid Id);

    private sealed record MoneyValue(decimal Amount, string Currency);

    private sealed record InstallmentRow(int Number, DateOnly Date, MoneyValue Amount, bool Paid);

    private sealed record PlanRow(
        Guid Id,
        Guid AccountId,
        Guid? PurchaseTransactionId,
        DateOnly PurchaseDate,
        string Description,
        MoneyValue TotalAmount,
        int InstallmentCount,
        int InstallmentsAlreadyPaid,
        DateOnly FirstInstallmentDate,
        decimal? AnnualRate,
        MoneyValue InstallmentAmount,
        int InstallmentsPaidOrDue,
        MoneyValue RemainingAmount,
        DateOnly? NextInstallmentDate,
        bool IsActive,
        IReadOnlyList<InstallmentRow> Schedule);
}
