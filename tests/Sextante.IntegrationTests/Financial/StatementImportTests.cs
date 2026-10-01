using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sextante.Modules.Financial.Application.Tests.StatementConverters;
using static Sextante.IntegrationTests.Financial.CsvImportTestHelpers;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.6 (5.5) — upload de extratos XLSX / PDF (layouts A e B) / JSON pelo mesmo
/// endpoint do CSV: conversão validada, corte de overlap, confirm igual ao do CSV e
/// "uma validação que falha não importa nada". Extratos sintéticos (D15).
/// </summary>
public sealed class StatementImportTests : IClassFixture<IdentityIntegrationFixture>
{
    private const short Checking = 0;
    private const short CreditCard = 3;

    private readonly IdentityIntegrationFixture _fixture;

    public StatementImportTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    // ---- Fixtures ------------------------------------------------------------------------

    private static DateTime D(int month, int day) => new(2026, month, day);

    private static object?[] R(params object?[] cells) => cells;

    private static List<object?[]> AccountMovements() => new()
    {
        R(D(9, 1), D(9, 1), "PAG BXVAL- 0000 VIAVERDE", -20.3, 864.31),
        R(D(9, 2), D(9, 2), "TRF. P/O  EMPRESA EXEMPLO LDA", 616, 1480.31),
        R(D(9, 3), D(9, 3), "COMPRA 0000 LIVRARIA EXEMPLO", -37.03, 1443.28),
        R(D(9, 7), D(9, 7), "TRF. P/ JOAO EXEMPLO", -90.96, 1352.32),
        R(D(9, 7), D(9, 7), "TRF. P/ JOAO EXEMPLO", -90.96, 1261.36),
        R(D(10, 1), D(10, 1), "DD GINASIO EXEMPLO 0000", -45, 1216.36),
    };

    private static byte[] Workbook(IEnumerable<object?[]> movements)
    {
        var rows = new List<object?[]>
        {
            R("HISTÓRICO DE CONTA NÚMERO 0000"),
            R("Moeda:", "EUR"),
            R("Tipo:", "Todos"),
            R("Data de:", D(9, 1)),
            R("Data até:", D(10, 1)),
            R((object?)null),
            R("Data Lanc.", "Data Valor", "Descrição", "Valor", "Saldo"),
        };
        rows.AddRange(movements);
        return XlsxFixtureBuilder.Build(rows);
    }

    private static string CoverflexJson()
    {
        JsonObject Movement(string executedAt, string description, long cents, bool isDebit, long? before, long? after, string status = "confirmed")
            => new()
            {
                ["status"] = status,
                ["description"] = description,
                ["amount"] = new JsonObject { ["currency"] = "EUR", ["amount"] = cents },
                ["is_debit"] = isDebit,
                ["executed_at"] = executedAt,
                ["balance_before"] = before is null ? null : new JsonObject { ["currency"] = "EUR", ["amount"] = before },
                ["balance_after"] = after is null ? null : new JsonObject { ["currency"] = "EUR", ["amount"] = after },
            };

        var list = new JsonArray(
            Movement("2026-09-26T07:55:17Z", "COMPRA PENDENTE EXEMPLO", 189, true, null, null, "pending"),
            Movement("2026-01-07T09:00:00Z", "COMPRA SUPERMERCADO EXEMPLO", 13247, true, 20208, 6961),
            Movement("2026-01-06T22:30:00Z", "COMPRA PADARIA EXEMPLO", 937, true, 21145, 20208),
            Movement("2026-01-06T09:53:29Z", "CVFX79X5V490YH6K COVERFLEX TOPUP ITEMID:13AE6BC8", 21120, false, 25, 21145));
        return new JsonObject { ["movements"] = new JsonObject { ["list"] = list } }.ToJsonString();
    }

    private static byte[] CardPdfA() => CardPdfFixtureBuilder.LayoutA(
        [
            new("2026/07/31", "2026/08/01", "COMPRA 0000 PADARIA EXEMPLO", 2.00m),
            new("2026/08/03", "2026/08/04", "COMPRA 0000 CONTINENTE BOM DIA 1150 CONT", 34.20m),
            new("2026/08/12", "2026/08/13", "COMPRA 0000 PORTATIL EXEMPLO", 1439.00m),
            new("2026/08/12", "2026/08/12", ">PAGAMENTO CARTAO DE CREDITO", 500.00m, IsCredit: true),
        ],
        new FixtureSummary(1000.00m, 500.00m, 1475.20m, 1975.20m)).ToPdf();

    private static byte[] CardPdfB() => CardPdfFixtureBuilder.LayoutB(
        [
            new("09/08", "09/09", "COMPRA 0000 SERVICO EXEMPLO", 22.14m, Continuation: ["SUB DUBLIN"]),
            new("09/12", "09/14", "COMPRA 0000 SUPERMERCADO EXEMPLO", 13.16m, Network: "MB", Continuation: ["DIA ALCA CONT"]),
            new("09/01", "09/01", ">PAGAMENTO CARTAO DE CREDITO", 668.66m, IsCredit: true),
        ],
        new FixtureSummary(2409.17m, 668.66m, 35.30m, 1775.81m)).ToPdf();

    // ---- Helpers -------------------------------------------------------------------------

    private async Task<HttpClient> NewClientAsync(string prefix)
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, prefix);
        await CreateCategoryAsync(client, "Diversos", kind: 0);
        await CreateCategoryAsync(client, "Entradas", kind: 1);
        return client;
    }

    private static Task<HttpResponseMessage> PostStatementAsync(HttpClient client, byte[] bytes, string fileName, Guid accountId)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return client.PostAsync($"/api/financial/imports/upload?accountId={accountId}", form);
    }

    private static async Task<JsonElement> UploadStatementAsync(HttpClient client, byte[] bytes, string fileName, Guid accountId)
    {
        var response = await PostStatementAsync(client, bytes, fileName, accountId);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<int> BatchCountAsync(HttpClient client)
        => (await client.GetFromJsonAsync<JsonElement>("/api/financial/imports")).GetArrayLength();

    // ---- XLSX ----------------------------------------------------------------------------

    [Fact]
    public async Task Xlsx_upload_returns_the_statement_summary_and_confirm_creates_the_transactions()
    {
        var client = await NewClientAsync("stmt-xlsx");
        var account = await CreateAccountAsync(client, "Conta à ordem", Checking, openingBalance: 884.61m);

        var upload = await UploadStatementAsync(client, Workbook(AccountMovements()), "mov.xlsx", account);

        upload.GetProperty("totalRowCount").GetInt32().Should().Be(6);
        var statement = upload.GetProperty("statement");
        statement.GetProperty("format").GetString().Should().Be("ActivoBankAccountXlsx");
        statement.GetProperty("rowsTrimmed").GetInt32().Should().Be(0);
        statement.GetProperty("checks").EnumerateArray().Should().OnlyContain(c => c.GetProperty("passed").GetBoolean());
        statement.GetProperty("balanceAfter").GetDecimal().Should().Be(1216.36m);
        PreviewRows(upload).Should().OnlyContain(r => r.GetProperty("error").ValueKind == JsonValueKind.Null);

        var confirm = await ConfirmAsync(client, upload);

        confirm.GetProperty("importedRows").GetInt32().Should().Be(6);
        (await ListTransactionsAsync(client, account)).Should().HaveCount(6);
        (await CurrentBalanceAsync(client, account)).Should().Be(1216.36m);
    }

    [Fact]
    public async Task Reuploading_an_imported_xlsx_gives_zero_new_rows()
    {
        var client = await NewClientAsync("stmt-xlsx-reup");
        var account = await CreateAccountAsync(client, "Conta à ordem", Checking, openingBalance: 884.61m);
        var xlsx = Workbook(AccountMovements());
        await ConfirmAsync(client, await UploadStatementAsync(client, xlsx, "mov.xlsx", account));

        var again = await UploadStatementAsync(client, xlsx, "mov.xlsx", account);

        again.GetProperty("totalRowCount").GetInt32().Should().Be(0);
        again.GetProperty("statement").GetProperty("rowsTrimmed").GetInt32().Should().Be(6);
        (await ListTransactionsAsync(client, account)).Should().HaveCount(6);
    }

    [Fact]
    public async Task Partially_overlapping_xlsx_imports_only_new_rows_and_keeps_two_identical_same_day_movements()
    {
        var client = await NewClientAsync("stmt-xlsx-part");
        var account = await CreateAccountAsync(client, "Conta à ordem", Checking, openingBalance: 884.61m);
        // 1.º extrato: até à 1.ª das duas transferências idênticas de 07/09.
        await ConfirmAsync(client, await UploadStatementAsync(client, Workbook(AccountMovements().Take(4)), "mov1.xlsx", account));

        var second = await UploadStatementAsync(client, Workbook(AccountMovements()), "mov2.xlsx", account);

        second.GetProperty("totalRowCount").GetInt32().Should().Be(2);
        second.GetProperty("statement").GetProperty("rowsTrimmed").GetInt32().Should().Be(4);
        var rows = PreviewRows(second);
        rows[0].GetProperty("values")[2].GetString().Should().Be("TRF. P/ JOAO EXEMPLO");
        rows[0].GetProperty("isDuplicate").GetBoolean().Should().BeFalse("o corte é pelo saldo, não pelo dedup heurístico");

        await ConfirmAsync(client, second);
        (await ListTransactionsAsync(client, account)).Should().HaveCount(6);
        (await CurrentBalanceAsync(client, account)).Should().Be(1216.36m);
    }

    [Fact]
    public async Task Xlsx_with_a_tampered_balance_returns_400_and_creates_no_batch()
    {
        var client = await NewClientAsync("stmt-xlsx-bad");
        var account = await CreateAccountAsync(client, "Conta à ordem", Checking, openingBalance: 884.61m);
        var movements = AccountMovements();
        movements[3][4] = 1352.99; // devia ser 1352.32

        var response = await PostStatementAsync(client, Workbook(movements), "mov.xlsx", account);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("não encadeia").And.Contain("1352,32").And.Contain("1352,99");
        (await BatchCountAsync(client)).Should().Be(0);
        (await ListTransactionsAsync(client, account)).Should().BeEmpty();
    }

    [Fact]
    public async Task Xlsx_extension_with_other_content_is_refused()
    {
        var client = await NewClientAsync("stmt-xlsx-fake");
        var account = await CreateAccountAsync(client, "Conta à ordem", Checking);

        var response = await PostStatementAsync(client, "Data;Valor\n01/09/2026;1,00"u8.ToArray(), "mov.xlsx", account);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("não corresponde à extensão");
        (await BatchCountAsync(client)).Should().Be(0);
    }

    [Fact]
    public async Task Unsupported_extension_is_refused_with_the_new_message()
    {
        var client = await NewClientAsync("stmt-ext");
        var account = await CreateAccountAsync(client, "Conta à ordem", Checking);

        var response = await PostStatementAsync(client, "x"u8.ToArray(), "extrato.txt", account);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(".csv, .xlsx, .pdf e .json");
    }

    // ---- PDF do cartão -------------------------------------------------------------------

    [Fact]
    public async Task Card_pdf_layout_a_imports_and_leaves_the_balance_at_minus_the_closing_debt()
    {
        var client = await NewClientAsync("stmt-pdf-a");
        var card = await CreateAccountAsync(client, "Cartão", CreditCard, openingBalance: -1000.00m);

        var upload = await UploadStatementAsync(client, CardPdfA(), "extrato.pdf", card);

        upload.GetProperty("statement").GetProperty("format").GetString().Should().Be("ActivoBankCardPdf");
        upload.GetProperty("statement").GetProperty("checks").GetArrayLength().Should().Be(3);
        upload.GetProperty("totalRowCount").GetInt32().Should().Be(4);

        await ConfirmAsync(client, upload);

        (await CurrentBalanceAsync(client, card)).Should().Be(-1975.20m);
    }

    [Fact]
    public async Task Card_pdf_layout_b_imports_with_the_year_taken_from_the_period()
    {
        var client = await NewClientAsync("stmt-pdf-b");
        var card = await CreateAccountAsync(client, "Cartão", CreditCard, openingBalance: -2409.17m);

        var upload = await UploadStatementAsync(client, CardPdfB(), "extrato.pdf", card);
        await ConfirmAsync(client, upload);

        PreviewRows(upload)[0].GetProperty("values")[0].GetString().Should().Be("08/09/2026");
        (await CurrentBalanceAsync(client, card)).Should().Be(-1775.81m);
    }

    [Fact]
    public async Task Reuploading_an_imported_card_pdf_gives_zero_new_rows()
    {
        var client = await NewClientAsync("stmt-pdf-reup");
        var card = await CreateAccountAsync(client, "Cartão", CreditCard, openingBalance: -1000.00m);
        await ConfirmAsync(client, await UploadStatementAsync(client, CardPdfA(), "extrato.pdf", card));

        var again = await UploadStatementAsync(client, CardPdfA(), "extrato.pdf", card);

        again.GetProperty("totalRowCount").GetInt32().Should().Be(0);
        (await ListTransactionsAsync(client, card)).Should().HaveCount(4);
    }

    [Fact]
    public async Task Card_pdf_with_summary_totals_that_do_not_match_returns_400_and_creates_no_batch()
    {
        var client = await NewClientAsync("stmt-pdf-bad");
        var card = await CreateAccountAsync(client, "Cartão", CreditCard, openingBalance: -1000.00m);
        var bad = CardPdfFixtureBuilder.LayoutA(
            [new("2026/08/03", "2026/08/04", "COMPRA 0000 LOJA EXEMPLO", 10.00m)],
            new FixtureSummary(1000.00m, 0m, 99.00m, 1099.00m)).ToPdf();

        var response = await PostStatementAsync(client, bad, "extrato.pdf", card);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Total de débitos").And.Contain("99,00").And.Contain("10,00");
        (await BatchCountAsync(client)).Should().Be(0);
    }

    [Fact]
    public async Task Corrupted_pdf_is_a_400_not_a_500()
    {
        var client = await NewClientAsync("stmt-pdf-corrupt");
        var card = await CreateAccountAsync(client, "Cartão", CreditCard);

        var response = await PostStatementAsync(client, "%PDF-1.7 corrompido"u8.ToArray(), "extrato.pdf", card);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BatchCountAsync(client)).Should().Be(0);
    }

    // ---- Coverflex -----------------------------------------------------------------------

    [Fact]
    public async Task Coverflex_json_imports_confirmed_movements_and_reports_the_pending_one()
    {
        var client = await NewClientAsync("stmt-cf");
        var account = await CreateAccountAsync(client, "Coverflex", Checking, openingBalance: 0.25m);

        var upload = await UploadStatementAsync(client, Encoding.UTF8.GetBytes(CoverflexJson()), "coverflex.json", account);

        upload.GetProperty("totalRowCount").GetInt32().Should().Be(3);
        upload.GetProperty("statement").GetProperty("pendingIgnored").GetInt32().Should().Be(1);
        PreviewRows(upload)[0].GetProperty("values")[2].GetString().Should().Be("COVERFLEX TOPUP");

        await ConfirmAsync(client, upload);

        (await CurrentBalanceAsync(client, account)).Should().Be(69.61m);
    }

    [Fact]
    public async Task Currency_different_from_the_account_is_refused()
    {
        var client = await NewClientAsync("stmt-cur");
        var usd = await CreateAccountAsync(client, "Conta USD", Checking, currency: "USD");

        var response = await PostStatementAsync(client, Workbook(AccountMovements()), "mov.xlsx", usd);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("EUR").And.Contain("USD");
        (await BatchCountAsync(client)).Should().Be(0);
    }

    // ---- Multi-tenancy --------------------------------------------------------------------

    [Fact]
    public async Task Statement_batch_of_one_tenant_is_not_visible_or_confirmable_by_another()
    {
        var clientA = await NewClientAsync("stmt-mt-a");
        var clientB = await NewClientAsync("stmt-mt-b");
        var accountA = await CreateAccountAsync(clientA, "Conta A", Checking, openingBalance: 884.61m);
        var upload = await UploadStatementAsync(clientA, Workbook(AccountMovements()), "mov.xlsx", accountA);

        (await BatchCountAsync(clientB)).Should().Be(0);
        var confirmByB = await clientB.PostAsJsonAsync(
            $"/api/financial/imports/{upload.GetProperty("batchId").GetString()}/confirm",
            new { includeDuplicates = Array.Empty<Guid>(), includeBeforeOpeningBalance = false });
        confirmByB.IsSuccessStatusCode.Should().BeFalse();

        var accountB = await CreateAccountAsync(clientB, "Conta B", Checking);
        var uploadToForeignAccount = await PostStatementAsync(clientB, Workbook(AccountMovements()), "mov.xlsx", accountA);
        uploadToForeignAccount.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListTransactionsAsync(clientB, accountB)).Should().BeEmpty();
    }
}
