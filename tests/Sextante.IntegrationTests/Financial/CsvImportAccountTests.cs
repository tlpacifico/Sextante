using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using static Sextante.IntegrationTests.Financial.CsvImportTestHelpers;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 grupo 7 (§7.1, §7.3, §7.4) — conta de destino obrigatória no
/// upload, linhas anteriores ao saldo inicial, duplicados por conta,
/// direção pelo sinal e definições do preview usadas no confirm (Q3).
/// </summary>
public sealed class CsvImportAccountTests : IClassFixture<IdentityIntegrationFixture>
{
    private const string Csv =
        ActivoHeader + "\n"
        + "27/08/2026;27/08/2026;COMPRA PADARIA EXEMPLO;-3,20;1 000,00\n"
        + "02/09/2026;02/09/2026;TRANSFERENCIA - VENCIMENTO;1 500,00;2 500,00\n"
        + "15/09/2026;15/09/2026;COMPRA SUPERMERCADO EXEMPLO;-62,35;2 437,65\n";

    private readonly IdentityIntegrationFixture _fixture;

    public CsvImportAccountTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Upload_without_account_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-acc-none");
        var profile = await CreateActivoProfileAsync(client);

        var response = await PostUploadAsync(client, Csv, accountId: null, profile);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_with_other_tenant_account_returns_400()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-acc-mtA");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-acc-mtB");
        var accountA = await CreateAccountAsync(clientA, "Conta A");
        var profileB = await CreateActivoProfileAsync(clientB);

        var response = await PostUploadAsync(clientB, Csv, accountA, profileB);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rows_go_to_the_chosen_account()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-acc-chosen");
        await CreateAccountAsync(client, "A primeira");
        var second = await CreateAccountAsync(client, "B segunda");
        await CreateCategoryAsync(client, "Diversos", kind: 0);
        await CreateCategoryAsync(client, "Entradas", kind: 1);
        var profile = await CreateActivoProfileAsync(client);

        var result = await ImportAsync(client, Csv, second, profile);

        result.GetProperty("importedRows").GetInt32().Should().Be(3);
        var all = await ListTransactionsAsync(client);
        all.Should().HaveCount(3);
        all.Should().OnlyContain(t => t.GetProperty("accountId").GetGuid() == second);
    }

    [Fact]
    public async Task Rows_before_opening_balance_are_flagged_and_excluded_by_default()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-acc-obd");
        var account = await CreateAccountAsync(
            client, "Conta", openingBalance: 1000m, openingBalanceDate: new DateOnly(2026, 8, 28));
        await CreateCategoryAsync(client, "Diversos", kind: 0);
        await CreateCategoryAsync(client, "Entradas", kind: 1);
        var profile = await CreateActivoProfileAsync(client);

        var upload = await UploadAsync(client, Csv, account, profile);
        var flags = PreviewRows(upload).Select(r => r.GetProperty("isBeforeOpeningBalance").GetBoolean()).ToList();
        flags.Should().Equal(true, false, false);

        var result = await ConfirmAsync(client, upload);
        result.GetProperty("importedRows").GetInt32().Should().Be(2);
        result.GetProperty("skippedBeforeOpeningBalance").GetInt32().Should().Be(1);
        (await ListTransactionsAsync(client, account))
            .Should().NotContain(t => t.GetProperty("description").GetString() == "COMPRA PADARIA EXEMPLO");
        (await CurrentBalanceAsync(client, account)).Should().Be(1000m + 1500m - 62.35m);

        var single = ActivoHeader + "\n27/08/2026;27/08/2026;COMPRA PADARIA EXEMPLO;-3,20;1 000,00\n";
        var forced = await ImportAsync(client, single, account, profile, includeBeforeOpeningBalance: true);
        forced.GetProperty("importedRows").GetInt32().Should().Be(1);
        forced.GetProperty("skippedBeforeOpeningBalance").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Duplicates_are_per_account()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-acc-dup");
        var accountA = await CreateAccountAsync(client, "Conta A");
        var accountB = await CreateAccountAsync(client, "Conta B");
        await CreateCategoryAsync(client, "Diversos", kind: 0);
        await CreateCategoryAsync(client, "Entradas", kind: 1);
        var profile = await CreateActivoProfileAsync(client);
        await ImportAsync(client, Csv, accountA, profile);

        var otherAccount = await UploadAsync(client, Csv, accountB, profile);
        PreviewRows(otherAccount).Should().OnlyContain(r => !r.GetProperty("isDuplicate").GetBoolean());

        var sameAccount = await UploadAsync(client, Csv, accountA, profile);
        PreviewRows(sameAccount).Should().OnlyContain(r => r.GetProperty("isDuplicate").GetBoolean());
    }

    [Fact]
    public async Task Direction_follows_sign_even_if_rule_category_kind_differs()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-acc-dir");
        var account = await CreateAccountAsync(client, "Conta");
        await CreateCategoryAsync(client, "Diversos", kind: 0);
        var income = await CreateCategoryAsync(client, "Entradas", kind: 1);
        await CreateCategoryRuleAsync(client, "SUPERMERCADO", income, priority: 1);
        var profile = await CreateActivoProfileAsync(client);

        await ImportAsync(client, Csv, account, profile);

        var tx = (await ListTransactionsAsync(client, account))
            .Single(t => t.GetProperty("description").GetString() == "COMPRA SUPERMERCADO EXEMPLO");
        tx.GetProperty("direction").GetString().Should().Be("Outflow");
        tx.GetProperty("categoryId").GetGuid().Should().NotBe(income);
    }

    [Fact]
    public async Task Confirm_uses_mapping_chosen_in_preview()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-acc-map");
        var account = await CreateAccountAsync(client, "Conta");
        await CreateCategoryAsync(client, "Diversos", kind: 0);
        const string csv = "Quando;O quê;Quanto\n05/09/2026;LOJA EXEMPLO;-12,50\n";

        var upload = await UploadAsync(client, csv, account, profileId: null);
        var batchId = upload.GetProperty("batchId").GetString();
        var preview = await client.PutAsJsonAsync($"/api/financial/imports/{batchId}/preview", new
        {
            columnMappings = new[]
            {
                new { csvColumnName = "Quando", transactionField = "Date" },
                new { csvColumnName = "O quê", transactionField = "Description" },
                new { csvColumnName = "Quanto", transactionField = "Amount" },
            },
            delimiter = ";",
            hasHeaderRow = true,
            dateFormat = "dd/MM/yyyy",
            decimalSeparator = ",",
            skipRows = 0,
        });
        await EnsureSuccessAsync(preview);
        var updated = await preview.Content.ReadFromJsonAsync<JsonElement>();
        PreviewRows(updated).Single().GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);

        var result = await ConfirmAsync(client, updated);

        result.GetProperty("importedRows").GetInt32().Should().Be(1);
        var tx = (await ListTransactionsAsync(client, account)).Single();
        tx.GetProperty("amount").GetProperty("amount").GetDecimal().Should().Be(12.50m);
        tx.GetProperty("direction").GetString().Should().Be("Outflow");
        tx.GetProperty("description").GetString().Should().Be("LOJA EXEMPLO");
        tx.GetProperty("occurredAt").GetDateTimeOffset().UtcDateTime.Date.Should().Be(new DateTime(2026, 9, 5));
    }

    [Fact]
    public async Task Unknown_account_name_in_column_is_row_error()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "imp-acc-col");
        var account = await CreateAccountAsync(client, "Conta");
        await CreateCategoryAsync(client, "Diversos", kind: 0);
        const string csv = "Data;Descrição;Valor;Conta\n05/09/2026;LOJA EXEMPLO;-12,50;Inexistente\n";

        var upload = await UploadAsync(client, csv, account, profileId: null);
        var batchId = upload.GetProperty("batchId").GetString();
        var preview = await client.PutAsJsonAsync($"/api/financial/imports/{batchId}/preview", new
        {
            columnMappings = new[]
            {
                new { csvColumnName = "Data", transactionField = "Date" },
                new { csvColumnName = "Descrição", transactionField = "Description" },
                new { csvColumnName = "Valor", transactionField = "Amount" },
                new { csvColumnName = "Conta", transactionField = "Account" },
            },
            delimiter = ";",
            hasHeaderRow = true,
            dateFormat = "dd/MM/yyyy",
            decimalSeparator = ",",
            skipRows = 0,
        });
        await EnsureSuccessAsync(preview);
        var updated = await preview.Content.ReadFromJsonAsync<JsonElement>();

        PreviewRows(updated).Single().GetProperty("error").GetString().Should().Contain("Inexistente");
    }
}
