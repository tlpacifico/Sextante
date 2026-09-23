using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 grupo 7 — isolamento entre tenants das regras de transferência
/// e da procura de contrapernas no import. Classe própria (fixture e rate
/// limiter de auth próprios): FinancialMultiTenancyTests já está perto do
/// limite de 30 signups/minuto.
/// </summary>
public sealed class ImportTransferMultiTenancyTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public ImportTransferMultiTenancyTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Transfer_rule_target_must_belong_to_tenant()
    {
        // Phase 6.5 grupo 7 — uma regra de transferência não pode apontar
        // para uma conta de outro tenant (soft reference validada no handler).
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-rulexfer");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-rulexfer");
        var cardA = await CsvImportTestHelpers.CreateAccountAsync(clientA, "Cartão A", type: 3);

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
    public async Task Import_transfer_matching_is_tenant_scoped()
    {
        // Phase 6.5 grupo 7 — a procura da contraperna nunca vê transações de
        // outro tenant, mesmo com a mesma data e valor.
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtA-impxfer");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mtB-impxfer");
        var checkingA = await CsvImportTestHelpers.CreateAccountAsync(clientA, "Conta A");
        var cardA = await CsvImportTestHelpers.CreateAccountAsync(clientA, "Cartão A", type: 3);
        await CsvImportTestHelpers.CreateCategoryAsync(clientA, "Diversos", kind: 0);
        await CsvImportTestHelpers.CreateTransferRuleAsync(clientA, "PAGAMENTO CARTAO", cardA, priority: 1);
        var profileA = await CsvImportTestHelpers.CreateActivoProfileAsync(clientA);

        var cardB = await CsvImportTestHelpers.CreateAccountAsync(clientB, "Cartão B", type: 3);
        var incomeB = await CsvImportTestHelpers.CreateCategoryAsync(clientB, "Entradas", kind: 1);
        var txB = await clientB.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId = cardB,
            categoryId = incomeB,
            occurredAt = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero),
            amount = 450m,
            currency = (string?)null,
            description = "Pagamento recebido",
            tags = (string[]?)null,
        });
        txB.EnsureSuccessStatusCode();

        var csv = CsvImportTestHelpers.ActivoHeader + "\n"
            + "11/09/2026;11/09/2026;VIS PAGAMENTO CARTAO DE CREDITO;-450,00;0,00\n";
        var result = await CsvImportTestHelpers.ImportAsync(clientA, csv, checkingA, profileA);

        result.GetProperty("transfersCreated").GetInt32().Should().Be(1);
        result.GetProperty("transfersLinked").GetInt32().Should().Be(0);
        var itemsB = await CsvImportTestHelpers.ListTransactionsAsync(clientB, cardB);
        itemsB.Should().ContainSingle().Which.GetProperty("kind").GetString().Should().Be("Regular");
    }
}
