using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Sextante.Modules.Identity.Infrastructure.ExchangeRates;
using Sextante.Modules.Identity.Infrastructure.Jobs;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §0.5 — o confirm do import não publicava
/// <c>TransactionCreatedIntegrationEvent</c> (os alertas de orçamento não
/// disparavam para transações importadas) e assumia "EUR" como moeda
/// primária em vez da do tenant.
/// </summary>
public sealed class CsvImportEventsTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public CsvImportEventsTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Imported_transaction_triggers_budget_alert()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-evt");
        await CreateAccountAsync(client, "EUR");
        var categoryId = await CreateCategoryAsync(client, "Supermercado");
        await CreateRuleAsync(client, "CONTINENTE", categoryId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var budget = await client.PostAsJsonAsync("/api/financial/budgets", new
        {
            categoryId,
            year = today.Year,
            month = today.Month,
            limitAmount = 100m,
            limitCurrency = "EUR",
            alertThresholdPercent = 80,
            notes = (string?)null,
        });
        budget.EnsureSuccessStatusCode();

        await ImportSingleRowAsync(client, today, "COMPRA CONTINENTE LISBOA", "-90,00");

        // O subscriber de orçamentos corre de forma assíncrona (outbox).
        List<JsonElement>? alerts = null;
        for (var i = 0; i < 100; i++)
        {
            alerts = await client.GetFromJsonAsync<List<JsonElement>>("/api/financial/budgets/alerts/active");
            if (alerts!.Count > 0)
            {
                break;
            }

            await Task.Delay(100);
        }

        alerts.Should().ContainSingle().Which.GetProperty("threshold").GetInt32().Should().Be(80);
    }

    [Fact]
    public async Task Import_converts_to_the_tenant_primary_currency_not_to_EUR()
    {
        var stubProvider = new StubCurrencyProvider(new EcbSnapshot(
            DateOnly.FromDateTime(DateTime.UtcNow),
            new[] { new EcbDailyRate("BRL", 6.00m), new EcbDailyRate("USD", 1.10m) }));

        using var factory = _fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<ICurrencyProvider>(_ => stubProvider)));

        var client = await SignupAndLoginAsync(factory, "imp-brl");

        var tenant = await client.PutAsJsonAsync("/api/tenants/me", new { primaryCurrency = "BRL" });
        tenant.EnsureSuccessStatusCode();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EcbSnapshotJob>().RunAsync(CancellationToken.None);
        }

        await CreateAccountAsync(client, "EUR");
        await CreateCategoryAsync(client, "Supermercado");

        await ImportSingleRowAsync(client, DateOnly.FromDateTime(DateTime.UtcNow), "COMPRA LIDL", "-10,00");

        var list = await client.GetFromJsonAsync<JsonElement>("/api/financial/transactions");
        var tx = list.GetProperty("items").EnumerateArray().Single();
        tx.GetProperty("amount").GetProperty("currency").GetString().Should().Be("EUR");
        tx.GetProperty("exchangeRateToPrimary").GetDecimal()
            .Should().BeApproximately(6.00m, 0.0001m, "EUR→BRL vem do snapshot ECB do dia");
    }

    private static async Task ImportSingleRowAsync(HttpClient client, DateOnly date, string description, string amount)
    {
        var profileResponse = await client.PostAsJsonAsync("/api/financial/import-profiles", new
        {
            name = $"Perfil {Guid.NewGuid():N}",
            columnMappings = new[]
            {
                new { csvColumnName = "Data Valor", transactionField = "Date", defaultValue = (string?)null },
                new { csvColumnName = "Descrição", transactionField = "Description", defaultValue = (string?)null },
                new { csvColumnName = "Valor", transactionField = "Amount", defaultValue = (string?)null },
            },
            delimiter = ";",
            hasHeaderRow = true,
            dateFormat = "dd/MM/yyyy",
            decimalSeparator = ",",
            skipRows = 0,
        });
        profileResponse.EnsureSuccessStatusCode();
        var profileId = (await profileResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var csv = "Data Lanc.;Data Valor;Descrição;Valor;Saldo\n"
            + $"{date:dd/MM/yyyy};{date:dd/MM/yyyy};{description};{amount};100,00\n";
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "extrato.csv");

        var upload = await client.PostAsync($"/api/financial/imports/upload?importProfileId={profileId}", form);
        upload.EnsureSuccessStatusCode();
        var batchId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("batchId").GetString();

        var confirm = await client.PostAsJsonAsync(
            $"/api/financial/imports/{batchId}/confirm",
            new { includeDuplicates = Array.Empty<Guid>() });
        confirm.EnsureSuccessStatusCode();
        var result = await confirm.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("importedRows").GetInt32().Should().Be(1);
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string currency)
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta",
            type = 0,
            currency,
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

    private static async Task CreateRuleAsync(HttpClient client, string pattern, Guid categoryId)
    {
        var response = await client.PostAsJsonAsync("/api/financial/categorization-rules", new
        {
            name = pattern,
            pattern,
            matchType = "Contains",
            categoryId,
            priority = 1,
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpClient> SignupAndLoginAsync(WebApplicationFactory<Program> factory, string emailPrefix)
    {
        var client = factory.CreateClient();
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com";
        const string password = "Password123!extra";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password,
            tenantName = $"Tenant {emailPrefix}",
        });
        signup.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<FinancialTestHelpers.LoginBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private sealed class StubCurrencyProvider : ICurrencyProvider
    {
        private readonly EcbSnapshot _snapshot;

        public StubCurrencyProvider(EcbSnapshot snapshot) => _snapshot = snapshot;

        public Task<EcbSnapshot> FetchLatestAsync(CancellationToken cancellationToken) => Task.FromResult(_snapshot);
    }

    private sealed record IdRow(Guid Id);
}
