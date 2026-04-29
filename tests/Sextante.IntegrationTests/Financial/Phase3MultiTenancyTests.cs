using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Extensões Phase 3 dos testes de multi-tenancy: <c>Account.Currency</c>,
/// <c>Transaction.ExchangeRate*</c>, <c>Tenant.PrimaryCurrency</c> isoladas
/// por tenant; <c>shared.currencies</c> visível a todos sem RLS.
/// </summary>
public sealed class Phase3MultiTenancyTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public Phase3MultiTenancyTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Account_Currency_is_isolated_per_tenant()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "p3-curA");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "p3-curB");

        var createA = await clientA.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "USD A",
            type = 0,
            currency = "USD",
            openingBalanceAmount = 0m,
        });
        createA.EnsureSuccessStatusCode();
        var accountA = await createA.Content.ReadFromJsonAsync<AccountRow>();
        accountA!.Currency.Should().Be("USD");

        var listB = await clientB.GetFromJsonAsync<List<AccountRow>>("/api/financial/accounts");
        listB!.Should().NotContain(a => a.Id == accountA.Id);

        var getResponse = await clientB.GetAsync($"/api/financial/accounts/{accountA.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tenant_PrimaryCurrency_change_does_not_leak_to_other_tenant()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "p3-priA");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "p3-priB");

        // Tenant A muda a primary para BRL.
        var updateResponse = await clientA.PutAsJsonAsync("/api/tenants/me", new
        {
            primaryCurrency = "BRL",
        });
        updateResponse.EnsureSuccessStatusCode();

        // Tenant B continua com EUR (default).
        var settingsB = await clientB.GetFromJsonAsync<TenantSettingsRow>("/api/tenants/me");
        settingsB!.PrimaryCurrency.Should().Be("EUR",
            "mudança de tenant A não deve afetar tenant B.");

        // Tenant A vê a sua nova primary.
        var settingsA = await clientA.GetFromJsonAsync<TenantSettingsRow>("/api/tenants/me");
        settingsA!.PrimaryCurrency.Should().Be("BRL");
    }

    [Fact]
    public async Task Reference_currencies_are_shared_across_tenants()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "p3-refA");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "p3-refB");

        var listA = await clientA.GetFromJsonAsync<List<CurrencyRow>>("/api/currencies");
        var listB = await clientB.GetFromJsonAsync<List<CurrencyRow>>("/api/currencies");

        listA.Should().NotBeNull();
        listB.Should().NotBeNull();

        // Reference data partilhada — ambos os tenants vêem o mesmo seed.
        listA!.Count.Should().BeGreaterThan(150,
            "seed ISO 4217 popula ≥150 currencies activas.");
        listA.Count.Should().Be(listB!.Count);

        var codesA = listA.Select(c => c.Code).OrderBy(c => c).ToArray();
        var codesB = listB.Select(c => c.Code).OrderBy(c => c).ToArray();
        codesA.Should().BeEquivalentTo(codesB);
    }

    private sealed record AccountRow(Guid Id, string Name, string Currency);

    private sealed record TenantSettingsRow(Guid Id, string Name, string PrimaryCurrency);

    private sealed record CurrencyRow(string Code, string Name, string Symbol, int MinorUnits, bool IsActive);
}
