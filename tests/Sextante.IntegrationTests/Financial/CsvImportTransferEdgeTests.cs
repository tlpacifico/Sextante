using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using static Sextante.IntegrationTests.Financial.CsvImportTestHelpers;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 grupo 7 — achados da revisão profunda: preview desatualizado,
/// confirm repetido, regra só de um lado e CSV com as duas contas. Nenhum
/// destes casos pode duplicar uma transferência.
/// </summary>
public sealed class CsvImportTransferEdgeTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public CsvImportTransferEdgeTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    private static string CheckingCsv => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Data", "Import", "activo-conta-sintetico.csv"));

    private static string CardCsv => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Data", "Import", "activo-cartao-sintetico.csv"));

    private sealed record Setup(HttpClient Client, Guid Checking, Guid Card, Guid Profile);

    private async Task<Setup> SetupAsync(string prefix, bool cardRule = true)
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, prefix);
        var checking = await CreateAccountAsync(
            client, "Conta à ordem", type: 0, openingBalance: 1000m, openingBalanceDate: new DateOnly(2026, 8, 28));
        var card = await CreateAccountAsync(
            client, "Cartão", type: 3, openingBalance: -500m, openingBalanceDate: new DateOnly(2026, 8, 1));
        await CreateCategoryAsync(client, "Diversos", kind: 0);
        await CreateCategoryAsync(client, "Entradas", kind: 1);
        var profile = await CreateActivoProfileAsync(client);
        await CreateTransferRuleAsync(client, "PAGAMENTO CARTAO", card, priority: 1);
        if (cardRule)
        {
            await CreateTransferRuleAsync(client, "PAGAMENTO RECEBIDO", checking, priority: 2);
        }

        return new Setup(client, checking, card, profile);
    }

    [Fact]
    public async Task Stale_preview_does_not_import_a_recorded_transfer_as_regular()
    {
        // I1 — os dois extratos carregados antes de confirmar qualquer um;
        // confirma-se o do cartão primeiro.
        var s = await SetupAsync("imp-edge-stale");
        var accountUpload = await UploadAsync(s.Client, CheckingCsv, s.Checking, s.Profile);
        var cardUpload = await UploadAsync(s.Client, CardCsv, s.Card, s.Profile);
        await ConfirmAsync(s.Client, cardUpload);

        var result = await ConfirmAsync(s.Client, accountUpload);

        result.GetProperty("transfersAlreadyRecorded").GetInt32().Should().Be(1);
        result.GetProperty("transfersCreated").GetInt32().Should().Be(0);
        (await ListTransactionsAsync(s.Client, kind: "Transfer")).Should().HaveCount(2);
        (await ListTransactionsAsync(s.Client, s.Checking))
            .Should().NotContain(t => t.GetProperty("description").GetString()!.Contains("PAGAMENTO CARTAO")
                && t.GetProperty("kind").GetString() == "Regular");
        (await CurrentBalanceAsync(s.Client, s.Checking)).Should().Be(1987.65m);
    }

    [Fact]
    public async Task Confirming_the_same_batch_twice_is_rejected()
    {
        // I2 — um segundo confirm (ex.: depois de um timeout) não reimporta.
        var s = await SetupAsync("imp-edge-twice");
        var upload = await UploadAsync(s.Client, CheckingCsv, s.Checking, s.Profile);
        await ConfirmAsync(s.Client, upload);

        var again = await s.Client.PostAsJsonAsync(
            $"/api/financial/imports/{upload.GetProperty("batchId").GetString()}/confirm",
            new { includeDuplicates = Array.Empty<Guid>(), includeBeforeOpeningBalance = false });

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListTransactionsAsync(s.Client, s.Checking)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Rule_on_one_side_only_still_links_the_second_statement()
    {
        // I3 — só existe a regra do extrato da conta (exemplo da spec).
        var s = await SetupAsync("imp-edge-onerule", cardRule: false);
        await ImportAsync(s.Client, CheckingCsv, s.Checking, s.Profile);

        var upload = await UploadAsync(s.Client, CardCsv, s.Card, s.Profile);
        var received = PreviewRows(upload)
            .Single(r => r.GetProperty("values")[2].GetString()!.Contains("PAGAMENTO RECEBIDO"));
        received.GetProperty("transferStatus").GetString().Should().Be("AlreadyRecorded");
        received.GetProperty("isDuplicate").GetBoolean().Should().BeTrue();
        received.GetProperty("transferTargetAccountName").GetString().Should().Be("Conta à ordem");

        var result = await ConfirmAsync(s.Client, upload);

        result.GetProperty("transfersAlreadyRecorded").GetInt32().Should().Be(1);
        result.GetProperty("importedRows").GetInt32().Should().Be(2);
        (await ListTransactionsAsync(s.Client, kind: "Transfer")).Should().HaveCount(2);
        (await CurrentBalanceAsync(s.Client, s.Card)).Should().Be(-205.50m);
    }

    [Fact]
    public async Task Csv_with_both_accounts_creates_a_single_transfer()
    {
        // I4 — CSV com coluna Conta e os dois lados da mesma transferência.
        var s = await SetupAsync("imp-edge-multi");
        var profile = await CreateMultiAccountProfileAsync(s.Client);
        const string csv = "Data;Descrição;Valor;Conta\n"
            + "11/09/2026;VIS PAGAMENTO CARTAO DE CREDITO;-450,00;Conta à ordem\n"
            + "14/09/2026;PAGAMENTO RECEBIDO OBRIGADO;450,00;Cartão\n";

        var upload = await UploadAsync(s.Client, csv, s.Checking, profile);
        PreviewRows(upload).Select(r => r.GetProperty("transferStatus").GetString())
            .Should().Equal("CreateCounterpart", "AlreadyRecorded");

        var result = await ConfirmAsync(s.Client, upload);

        result.GetProperty("transfersCreated").GetInt32().Should().Be(1);
        result.GetProperty("transfersAlreadyRecorded").GetInt32().Should().Be(1);
        var legs = await ListTransactionsAsync(s.Client, kind: "Transfer");
        legs.Should().HaveCount(2);
        DateOnly.FromDateTime(legs.Single(l => l.GetProperty("accountId").GetGuid() == s.Card)
            .GetProperty("occurredAt").GetDateTimeOffset().UtcDateTime).Should().Be(new DateOnly(2026, 9, 14));
    }

    [Fact]
    public async Task Csv_with_card_row_first_links_it_within_the_batch()
    {
        // I4 — a linha do cartão vem primeiro e não tem regra: fica regular
        // no lote e a linha da conta liga-se a ela.
        var s = await SetupAsync("imp-edge-multi-rev", cardRule: false);
        var profile = await CreateMultiAccountProfileAsync(s.Client);
        const string csv = "Data;Descrição;Valor;Conta\n"
            + "14/09/2026;PAGAMENTO RECEBIDO OBRIGADO;450,00;Cartão\n"
            + "11/09/2026;VIS PAGAMENTO CARTAO DE CREDITO;-450,00;Conta à ordem\n";

        var result = await ImportAsync(s.Client, csv, s.Checking, profile);

        result.GetProperty("transfersLinked").GetInt32().Should().Be(1);
        result.GetProperty("importedRows").GetInt32().Should().Be(2);
        (await ListTransactionsAsync(s.Client, kind: "Transfer")).Should().HaveCount(2);
        (await ListTransactionsAsync(s.Client)).Should().HaveCount(2);
    }

    private static async Task<Guid> CreateMultiAccountProfileAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/financial/import-profiles", new
        {
            name = $"Multi {Guid.NewGuid():N}",
            columnMappings = new[]
            {
                new { csvColumnName = "Data", transactionField = "Date", defaultValue = (string?)null },
                new { csvColumnName = "Descrição", transactionField = "Description", defaultValue = (string?)null },
                new { csvColumnName = "Valor", transactionField = "Amount", defaultValue = (string?)null },
                new { csvColumnName = "Conta", transactionField = "Account", defaultValue = (string?)null },
            },
            delimiter = ";",
            hasHeaderRow = true,
            dateFormat = "dd/MM/yyyy",
            decimalSeparator = ",",
            skipRows = 0,
        });
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
