using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sextante.Modules.Identity.Infrastructure.ExchangeRates;
using Sextante.Modules.Identity.Infrastructure.Jobs;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Cenário end-to-end multi-moeda: snapshot ECB stub → conta USD numa
/// tenant com primary BRL → transação $300 persiste o ER do momento →
/// dashboard <c>converted</c> consolida em BRL; dashboard
/// <c>original</c> separa por moeda.
/// </summary>
public sealed class MultiCurrencyTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public MultiCurrencyTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Multi_currency_scenario_persists_rate_and_renders_both_view_modes()
    {
        var stubProvider = new StubCurrencyProvider(new EcbSnapshot(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new[]
            {
                new EcbDailyRate("USD", 1.10m),
                new EcbDailyRate("BRL", 6.00m),
                new EcbDailyRate("GBP", 0.85m),
            }));

        // Fork a factory with the stubbed provider so the EcbSnapshotJob
        // resolves the deterministic snapshot instead of hitting the live
        // ECB feed. WithWebHostBuilder boots a fresh host that shares the
        // same Postgres database (Testcontainers); migrations are
        // idempotent.
        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICurrencyProvider>(_ => stubProvider);
            });
        });

        var (client, _, _) = await SignupAndLoginAsync(factory, "phase3");

        // 1. Mudar tenant primary para BRL via PUT /api/tenants/me.
        var updateTenantResponse = await client.PutAsJsonAsync("/api/tenants/me", new
        {
            primaryCurrency = "BRL",
        });
        updateTenantResponse.EnsureSuccessStatusCode();

        // 2. Correr o snapshot job directamente via DI (substitui o
        //    POST /api/admin/exchange-rates/snapshot/run que precisaria
        //    de role SystemAdmin no JWT).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<EcbSnapshotJob>();
            await job.RunAsync(CancellationToken.None);
        }

        // 3. Criar conta USD e conta BRL.
        var usdAccount = await CreateAccountAsync(client, "USD Wallet", "USD", openingBalance: 0m);
        var brlAccount = await CreateAccountAsync(client, "BRL Conta", "BRL", openingBalance: 0m);

        // 4. Criar categorias income e expense.
        var income = await CreateCategoryAsync(client, kind: 1, name: "Salário");
        var expense = await CreateCategoryAsync(client, kind: 0, name: "Alimentação");

        // 5. Transação USD $300 (income).
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var usdTxResponse = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId = usdAccount,
            categoryId = income,
            occurredAt,
            amount = 300m,
            currency = (string?)null,
            description = "Freelance USD",
            tags = (string[]?)null,
        });
        usdTxResponse.EnsureSuccessStatusCode();
        var usdTransaction = await usdTxResponse.Content.ReadFromJsonAsync<TransactionRow>();

        usdTransaction!.Amount.Currency.Should().Be("USD");
        usdTransaction.ExchangeRateToPrimary.Should().NotBeNull();
        // USD→BRL = (1/1.10) × 6.00 = 6.00 / 1.10 ≈ 5.4545.
        var expectedRate = 6.00m / 1.10m;
        usdTransaction.ExchangeRateToPrimary!.Value
            .Should().BeApproximately(expectedRate, 0.0001m);
        usdTransaction.ExchangeRateAt.Should().NotBeNull();

        // 6. Transação BRL R$200 (expense) — primary == currency, ER null.
        var brlTxResponse = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId = brlAccount,
            categoryId = expense,
            occurredAt,
            amount = 200m,
            currency = (string?)null,
            description = "Mercado",
            tags = (string[]?)null,
        });
        brlTxResponse.EnsureSuccessStatusCode();
        var brlTransaction = await brlTxResponse.Content.ReadFromJsonAsync<TransactionRow>();

        brlTransaction!.Amount.Currency.Should().Be("BRL");
        brlTransaction.ExchangeRateToPrimary.Should().BeNull();
        brlTransaction.ExchangeRateAt.Should().BeNull();

        // 7. Summary converted (default).
        var converted = await client.GetFromJsonAsync<SummaryRow>(
            "/api/financial/transactions/summary?viewMode=converted");
        converted!.ViewMode.Should().Be("converted");
        converted.Income.Currency.Should().Be("BRL");
        converted.Expense.Currency.Should().Be("BRL");
        // Income = 300 × (6.00/1.10) ≈ 1636.36
        converted.Income.Amount.Should().BeApproximately(300m * expectedRate, 0.01m);
        converted.Expense.Amount.Should().Be(200m);
        converted.PerCurrency.Should().BeNull();

        // 8. Summary original.
        var original = await client.GetFromJsonAsync<SummaryRow>(
            "/api/financial/transactions/summary?viewMode=original");
        original!.ViewMode.Should().Be("original");
        original.PerCurrency.Should().NotBeNull();
        var perCurrency = original.PerCurrency!;
        perCurrency.Should().HaveCount(2);

        var usdRow = perCurrency.Single(r => r.Currency == "USD");
        usdRow.Income.Amount.Should().Be(300m);
        usdRow.Expense.Amount.Should().Be(0m);

        var brlRow = perCurrency.Single(r => r.Currency == "BRL");
        brlRow.Income.Amount.Should().Be(0m);
        brlRow.Expense.Amount.Should().Be(200m);

        // 9. By-category in original mode returns empty (donut hidden).
        var byCategoryOriginal = await client.GetFromJsonAsync<List<object>>(
            "/api/financial/transactions/by-category?kind=Expense&viewMode=original");
        byCategoryOriginal!.Should().BeEmpty();
    }

    private static async Task<(HttpClient Client, Guid TenantId, Guid UserId)> SignupAndLoginAsync(
        WebApplicationFactory<Program> factory,
        string emailPrefix)
    {
        var client = factory.CreateClient();
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com";
        const string password = "Password123!extra";

        var signupResponse = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password,
            tenantName = $"Tenant {emailPrefix}",
        });
        signupResponse.EnsureSuccessStatusCode();
        var signup = await signupResponse.Content.ReadFromJsonAsync<FinancialTestHelpers.SignupBody>();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<FinancialTestHelpers.LoginBody>();

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, signup!.TenantId, signup.UserId);
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string name, string currency, decimal openingBalance)
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name,
            type = 0,
            currency,
            openingBalanceAmount = openingBalance,
        });
        response.EnsureSuccessStatusCode();
        var row = await response.Content.ReadFromJsonAsync<IdRow>();
        return row!.Id;
    }

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
        var row = await response.Content.ReadFromJsonAsync<IdRow>();
        return row!.Id;
    }

    private sealed class StubCurrencyProvider : ICurrencyProvider
    {
        private readonly EcbSnapshot _snapshot;
        public StubCurrencyProvider(EcbSnapshot snapshot) => _snapshot = snapshot;
        public Task<EcbSnapshot> FetchLatestAsync(CancellationToken cancellationToken)
            => Task.FromResult(_snapshot);
    }

    private sealed record IdRow(Guid Id);

    private sealed record MoneyValue(decimal Amount, string Currency);

    private sealed record TransactionRow(
        Guid Id,
        Guid AccountId,
        Guid CategoryId,
        DateTimeOffset OccurredAt,
        MoneyValue Amount,
        decimal? ExchangeRateToPrimary,
        DateTimeOffset? ExchangeRateAt);

    private sealed record CurrencyTotalsRow(string Currency, MoneyValue Income, MoneyValue Expense, MoneyValue Net);

    private sealed record SummaryRow(
        MoneyValue Income,
        MoneyValue Expense,
        MoneyValue Net,
        string ViewMode,
        List<CurrencyTotalsRow>? PerCurrency);
}
