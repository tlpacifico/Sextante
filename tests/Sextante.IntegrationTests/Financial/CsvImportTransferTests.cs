using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using static Sextante.IntegrationTests.Financial.CsvImportTestHelpers;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 grupo 7 (§7.3, validation 12) — regras MarkAsTransfer no
/// import: a linha liga-se à contraperna existente ou cria-a; importar os
/// extratos da conta e do cartão (por qualquer ordem) não duplica a
/// transferência. Fixtures sintéticas no formato ActivoBank (sem dados reais).
/// </summary>
public sealed class CsvImportTransferTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public CsvImportTransferTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    private static string CheckingCsv => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Data", "Import", "activo-conta-sintetico.csv"));

    private static string CardCsv => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Data", "Import", "activo-cartao-sintetico.csv"));

    private sealed record Setup(HttpClient Client, Guid Checking, Guid Card, Guid Profile, Guid Income, Guid CardRule);

    /// <summary>
    /// Conta à ordem (1 000,00 a 28/08) e cartão (−500,00 a 01/08), regras
    /// "PAGAMENTO CARTAO" → cartão e "PAGAMENTO RECEBIDO" → conta.
    /// </summary>
    private async Task<Setup> SetupAsync(string prefix, string cardCurrency = "EUR", bool cardRuleToSelf = false)
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, prefix);
        var checking = await CreateAccountAsync(
            client, "Conta à ordem", type: 0, openingBalance: 1000m, openingBalanceDate: new DateOnly(2026, 8, 28));
        var card = await CreateAccountAsync(
            client, "Cartão", type: 3, currency: cardCurrency, openingBalance: -500m,
            openingBalanceDate: new DateOnly(2026, 8, 1));
        await CreateCategoryAsync(client, "Diversos", kind: 0);
        var income = await CreateCategoryAsync(client, "Entradas", kind: 1);
        var profile = await CreateActivoProfileAsync(client);
        var cardRule = await CreateTransferRuleAsync(client, "PAGAMENTO CARTAO", cardRuleToSelf ? checking : card, priority: 1);
        await CreateTransferRuleAsync(client, "PAGAMENTO RECEBIDO", checking, priority: 2);
        return new Setup(client, checking, card, profile, income, cardRule);
    }

    [Fact]
    public async Task Account_statement_creates_transfer_and_card_leg()
    {
        var s = await SetupAsync("imp-xfer-acc");

        var upload = await UploadAsync(s.Client, CheckingCsv, s.Checking, s.Profile);
        var payment = PreviewRows(upload).Single(r => r.GetProperty("values")[2].GetString()!.Contains("PAGAMENTO CARTAO"));
        payment.GetProperty("transferStatus").GetString().Should().Be("CreateCounterpart");
        payment.GetProperty("transferTargetAccountName").GetString().Should().Be("Cartão");

        var result = await ConfirmAsync(s.Client, upload);

        result.GetProperty("transfersCreated").GetInt32().Should().Be(1);
        result.GetProperty("importedRows").GetInt32().Should().Be(3);
        var cardLegs = await ListTransactionsAsync(s.Client, s.Card);
        var leg = cardLegs.Should().ContainSingle().Subject;
        leg.GetProperty("kind").GetString().Should().Be("Transfer");
        leg.GetProperty("direction").GetString().Should().Be("Inflow");
        leg.GetProperty("amount").GetProperty("amount").GetDecimal().Should().Be(450m);
        DateOf(leg).Should().Be(new DateOnly(2026, 9, 11));
        (await CurrentBalanceAsync(s.Client, s.Card)).Should().Be(-50m);
        (await CurrentBalanceAsync(s.Client, s.Checking)).Should().Be(1987.65m);
    }

    [Fact]
    public async Task Card_statement_after_account_links_without_duplicating()
    {
        var s = await SetupAsync("imp-xfer-a2c");
        await ImportAsync(s.Client, CheckingCsv, s.Checking, s.Profile);

        var upload = await UploadAsync(s.Client, CardCsv, s.Card, s.Profile);
        var received = PreviewRows(upload).Single(r => r.GetProperty("values")[2].GetString()!.Contains("PAGAMENTO RECEBIDO"));
        received.GetProperty("transferStatus").GetString().Should().Be("AlreadyRecorded");
        received.GetProperty("isDuplicate").GetBoolean().Should().BeTrue();

        var result = await ConfirmAsync(s.Client, upload);

        result.GetProperty("transfersAlreadyRecorded").GetInt32().Should().Be(1);
        result.GetProperty("importedRows").GetInt32().Should().Be(2);
        var cardTransfers = (await ListTransactionsAsync(s.Client, s.Card))
            .Where(t => t.GetProperty("kind").GetString() == "Transfer").ToList();
        cardTransfers.Should().ContainSingle();
        DateOf(cardTransfers[0]).Should().Be(new DateOnly(2026, 9, 14), "o extrato do cartão manda na data do cartão (Q1)");
        (await CurrentBalanceAsync(s.Client, s.Card)).Should().Be(-205.50m);
        (await ListTransactionsAsync(s.Client, kind: "Transfer")).Should().HaveCount(2);
    }

    [Fact]
    public async Task Card_statement_first_then_account()
    {
        var s = await SetupAsync("imp-xfer-c2a");
        var cardResult = await ImportAsync(s.Client, CardCsv, s.Card, s.Profile);
        cardResult.GetProperty("transfersCreated").GetInt32().Should().Be(1);

        var accountResult = await ImportAsync(s.Client, CheckingCsv, s.Checking, s.Profile);

        accountResult.GetProperty("transfersAlreadyRecorded").GetInt32().Should().Be(1);
        (await ListTransactionsAsync(s.Client, kind: "Transfer")).Should().HaveCount(2);
        var checkingLeg = (await ListTransactionsAsync(s.Client, s.Checking))
            .Single(t => t.GetProperty("kind").GetString() == "Transfer");
        DateOf(checkingLeg).Should().Be(new DateOnly(2026, 9, 11));
        (await CurrentBalanceAsync(s.Client, s.Checking)).Should().Be(1987.65m);
        (await CurrentBalanceAsync(s.Client, s.Card)).Should().Be(-205.50m);
    }

    [Fact]
    public async Task Links_existing_regular_counterpart()
    {
        var s = await SetupAsync("imp-xfer-link");
        var manual = await s.Client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId = s.Card,
            categoryId = s.Income,
            occurredAt = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero),
            amount = 450m,
            currency = (string?)null,
            description = "Pagamento recebido",
            tags = (string[]?)null,
        });
        await EnsureSuccessAsync(manual);
        var manualId = (await manual.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var result = await ImportAsync(s.Client, CheckingCsv, s.Checking, s.Profile);

        result.GetProperty("transfersLinked").GetInt32().Should().Be(1);
        result.GetProperty("transfersCreated").GetInt32().Should().Be(0);
        var cardTxs = await ListTransactionsAsync(s.Client, s.Card);
        cardTxs.Should().ContainSingle();
        cardTxs[0].GetProperty("id").GetGuid().Should().Be(manualId);
        cardTxs[0].GetProperty("kind").GetString().Should().Be("Transfer");
    }

    [Fact]
    public async Task Transfers_do_not_count_in_summary()
    {
        var s = await SetupAsync("imp-xfer-sum");
        await ImportAsync(s.Client, CheckingCsv, s.Checking, s.Profile);
        await ImportAsync(s.Client, CardCsv, s.Card, s.Profile);

        var summary = await s.Client.GetFromJsonAsync<JsonElement>(
            "/api/financial/transactions/summary?dateFrom=2026-09-01T00:00:00Z&dateTo=2026-09-30T23:59:59Z");

        summary.GetProperty("expense").GetProperty("amount").GetDecimal().Should().Be(62.35m + 35.50m);
        summary.GetProperty("income").GetProperty("amount").GetDecimal().Should().Be(1500m);
    }

    [Fact]
    public async Task Target_in_other_currency_imports_as_regular()
    {
        var s = await SetupAsync("imp-xfer-fx", cardCurrency: "USD");

        var upload = await UploadAsync(s.Client, CheckingCsv, s.Checking, s.Profile);
        PreviewRows(upload)
            .Single(r => r.GetProperty("values")[2].GetString()!.Contains("PAGAMENTO CARTAO"))
            .GetProperty("transferStatus").GetString().Should().Be("CurrencyMismatch");

        var result = await ConfirmAsync(s.Client, upload);

        result.GetProperty("transfersCreated").GetInt32().Should().Be(0);
        var payment = (await ListTransactionsAsync(s.Client, s.Checking))
            .Single(t => t.GetProperty("description").GetString()!.Contains("PAGAMENTO CARTAO"));
        payment.GetProperty("kind").GetString().Should().Be("Regular");
        payment.GetProperty("direction").GetString().Should().Be("Outflow");
    }

    [Fact]
    public async Task Rule_targeting_the_imported_account_imports_as_regular()
    {
        var s = await SetupAsync("imp-xfer-self", cardRuleToSelf: true);

        var upload = await UploadAsync(s.Client, CheckingCsv, s.Checking, s.Profile);
        PreviewRows(upload)
            .Single(r => r.GetProperty("values")[2].GetString()!.Contains("PAGAMENTO CARTAO"))
            .GetProperty("transferStatus").GetString().Should().Be("InvalidTarget");

        var result = await ConfirmAsync(s.Client, upload);

        result.GetProperty("importedRows").GetInt32().Should().Be(3);
        (await ListTransactionsAsync(s.Client, kind: "Transfer")).Should().BeEmpty();
    }

    [Fact]
    public async Task Imported_leg_keeps_rule_audit()
    {
        var s = await SetupAsync("imp-xfer-audit");
        await ImportAsync(s.Client, CheckingCsv, s.Checking, s.Profile);
        var leg = (await ListTransactionsAsync(s.Client, s.Checking))
            .Single(t => t.GetProperty("kind").GetString() == "Transfer");

        await using var conn = _fixture.OpenSuperuserConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT categorization_rule_id FROM financial.transactions WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", leg.GetProperty("id").GetGuid());

        ((Guid)(await cmd.ExecuteScalarAsync())!).Should().Be(s.CardRule);
    }

    private static DateOnly DateOf(JsonElement transaction)
        => DateOnly.FromDateTime(transaction.GetProperty("occurredAt").GetDateTimeOffset().UtcDateTime);
}
