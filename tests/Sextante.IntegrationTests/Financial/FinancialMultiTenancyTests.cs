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

    private sealed record IdRow(Guid Id);
}
