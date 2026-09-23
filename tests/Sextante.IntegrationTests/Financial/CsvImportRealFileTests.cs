using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sextante.IntegrationTests.Financial;
using Xunit.Abstractions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Teste de integração end-to-end: importação de um ficheiro CSV real
/// do ActivoBank com 200 transações, mapeamento de colunas, deteção de
/// duplicados e regras de categorização automática.
/// </summary>
public class CsvImportRealFileTests : IClassFixture<IdentityIntegrationFixture>, IAsyncLifetime
{
    private readonly IdentityIntegrationFixture _fixture;
    private readonly ITestOutputHelper _output;
    private HttpClient _client = null!;
    private Guid _tenantId;
    private string _csvFilePath;

    public CsvImportRealFileTests(IdentityIntegrationFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        _csvFilePath = Path.Combine(
            AppContext.BaseDirectory,
            "Data", "Import", "example-activo-bank.csv");
    }

    public async Task InitializeAsync()
    {
        (_client, _tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(
            _fixture, "csvimport");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FullCsvImportPipeline_RealActivoBankFile_ImportsAllRows()
    {
        // 0. Verify the CSV file exists
        File.Exists(_csvFilePath).Should().BeTrue(
            $"o ficheiro de teste deve existir em {_csvFilePath}");

        var csvContent = await File.ReadAllTextAsync(_csvFilePath);
        csvContent.Should().NotBeNullOrEmpty();
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        _output.WriteLine($"CSV file has {lines.Length} lines (including header)");

        // 1. Create account
        var accountResponse = await _client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "ActivoBank",
            type = 0, // Checking
            currency = (string?)null,
            openingBalanceAmount = 0m,
            // Phase 6.5 grupo 7 — o extrato começa em 2025-09; linhas
            // anteriores ao saldo inicial seriam excluídas por defeito.
            openingBalanceDate = "2025-01-01",
        });
        accountResponse.EnsureSuccessStatusCode();
        var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accountId = account.GetProperty("id").GetString()!;
        _output.WriteLine($"Created account: {accountId}");

        // 2. Create categories for auto-categorization
        var categories = new Dictionary<string, string>();
        var categoryDefs = new (string Name, string Kind)[]
        {
            ("Supermercado", "Expense"),
            ("Restauração", "Expense"),
            ("Transporte", "Expense"),
            ("Salário", "Income"),
            ("Renda", "Expense"),
            ("Serviços", "Expense"),
            ("Saúde", "Expense"),
            ("Vestuário", "Expense"),
            ("Lazer", "Expense"),
            ("Subscrições", "Expense"),
            ("Comunicações", "Expense"),
            ("Transferência", "Expense"),
            ("Reembolso", "Income"),
            ("Ginásio", "Expense"),
            ("Utilidades", "Expense"),
        };

        foreach (var def in categoryDefs)
        {
            var catResponse = await _client.PostAsJsonAsync("/api/financial/categories", new
            {
                name = def.Name,
                kind = def.Kind == "Expense" ? 0 : 1,
                iconName = "pi-tag",
                colorHex = "#64748B",
            });
            catResponse.EnsureSuccessStatusCode();
            var cat = await catResponse.Content.ReadFromJsonAsync<JsonElement>();
            categories[def.Name] = cat.GetProperty("id").GetString()!;
        }
        _output.WriteLine($"Created {categories.Count} categories");

        // 3. Create categorization rules (targeting ≥80% coverage)
        var rules = new (string Name, string Pattern, string MatchType, string Category)[]
        {
            ("Pingo Doce", "PINGO DOCE", "Contains", "Supermercado"),
            ("Continente", "CONTINENTE", "Contains", "Supermercado"),
            ("Lidl", "LIDL", "Contains", "Supermercado"),
            ("Mercearia", "MERCEARIA", "Contains", "Supermercado"),
            ("Pastelaria Aloma", "PASTELARIA ALOMA", "Contains", "Restauração"),
            ("McDonalds", "MCDONALDS", "Contains", "Restauração"),
            ("Pizzeria", "PIZZERIA", "Contains", "Restauração"),
            ("Gelados", "GELADOS", "Contains", "Restauração"),
            ("Via Verde", "VIAVERDE", "Contains", "Transporte"),
            ("Petrogal", "PETROGAL", "Contains", "Transporte"),
            ("Est. Serviço", "EST SERVICO", "Contains", "Transporte"),
            ("Marloconsult", "MARLOCONSULT", "Contains", "Salário"),
            ("Vencimento", "VENCIMENTO", "Contains", "Salário"),
            ("Renda", "TRF P/ renda", "StartsWith", "Renda"),
            ("NOS Comunicações", "NOS COMUNICACO", "Contains", "Comunicações"),
            ("Lisboa Ginásio", "LISBOA GINASIO", "Contains", "Ginásio"),
            ("Farmácia", "FARMACIA", "Contains", "Saúde"),
            ("Hospital", "HOSPITAL", "Contains", "Saúde"),
            ("Decathlon", "DECATHLON", "Contains", "Vestuário"),
            ("Lefties", "LEFTIES", "Contains", "Vestuário"),
            ("Cartão Crédito", "PAGAMENTO CARTAO", "Contains", "Subscrições"),
            ("Wise", "Wise", "Contains", "Transferência"),
            ("MB WAY", "TRF MB WAY", "StartsWith", "Transferência"),
            ("CTT", "CTT", "Contains", "Utilidades"),
            ("Eupago", "EUPAGO", "Contains", "Serviços"),
            ("Junta Freguesia", "JUNTA DE FREGUESIA", "Contains", "Serviços"),
            ("Via Directa", "VIA DIRECTA", "Contains", "Serviços"),
            ("Reembolso IRS", "REEMBOLSOS", "Contains", "Reembolso"),
            ("IGFEJ pagamentos", "INSTITUTO DE GEST", "Contains", "Reembolso"),
            ("IMT", "IMPOSTO UNICO", "Contains", "Serviços"),
            ("ATM", "LEV ATM", "StartsWith", "Lazer"),
        };

        for (int i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            var ruleResponse = await _client.PostAsJsonAsync("/api/financial/categorization-rules", new
            {
                name = rule.Name,
                pattern = rule.Pattern,
                matchType = rule.MatchType,
                categoryId = categories[rule.Category],
                priority = i + 1,
            });
            if (!ruleResponse.IsSuccessStatusCode)
            {
                var err = await ruleResponse.Content.ReadAsStringAsync();
                _output.WriteLine($"Rule '{rule.Name}' failed: {err}");
            }
            ruleResponse.EnsureSuccessStatusCode();
        }
        _output.WriteLine($"Created {rules.Length} categorization rules");

        // 4. Create ImportProfile
        var profileResponse = await _client.PostAsJsonAsync("/api/financial/import-profiles", new
        {
            name = "ActivoBank CSV",
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
        var profile = await profileResponse.Content.ReadFromJsonAsync<JsonElement>();
        var profileId = profile.GetProperty("id").GetString()!;
        _output.WriteLine($"Created import profile: {profileId}");

        // 5. Upload CSV file
        await using var fileStream = File.OpenRead(_csvFilePath);
        using var formData = new MultipartFormDataContent();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData.Add(fileContent, "file", "example-activo-bank.csv");

        var uploadResponse = await _client.PostAsync(
            $"/api/financial/imports/upload?importProfileId={profileId}&accountId={accountId}",
            formData);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var batchId = uploadResult.GetProperty("batchId").GetString()!;
        var totalRows = uploadResult.GetProperty("totalRowCount").GetInt32();
        var truncated = uploadResult.GetProperty("truncated").GetBoolean();
        var headers = uploadResult.GetProperty("headers").EnumerateArray()
            .Select(h => h.GetString()).ToArray();
        var previewRows = uploadResult.GetProperty("previewRows").EnumerateArray().ToArray();

        _output.WriteLine($"Uploaded CSV: batchId={batchId}, totalRows={totalRows}, truncated={truncated}");
        _output.WriteLine($"Headers: {string.Join(" | ", headers)}");
        _output.WriteLine($"Preview rows: {previewRows.Length}");

        // Assertions on upload
        batchId.Should().NotBeEmpty();
        totalRows.Should().Be(200, "CSV has 200 data rows (excluding header)");
        truncated.Should().BeFalse("200 rows is less than the 1000-row preview limit");
        headers.Should().Contain("Data Valor");
        headers.Should().Contain("Descrição");
        headers.Should().Contain("Valor");
        previewRows.Should().HaveCount(200);

        // 6. Count auto-categorized rows in preview
        var autoCategorizedCount = previewRows.Count(r =>
            r.GetProperty("isAutoCategorized").GetBoolean());
        var errorCount = previewRows.Count(r =>
            !string.IsNullOrWhiteSpace(r.GetProperty("error").GetString()));

        _output.WriteLine($"Preview: {autoCategorizedCount} auto-categorized, {errorCount} errors");
        autoCategorizedCount.Should().BeGreaterThan(0, "rules should match some rows");

        // 7. Confirm import
        var confirmResponse = await _client.PostAsJsonAsync(
            $"/api/financial/imports/{batchId}/confirm",
            new { includeDuplicates = Array.Empty<Guid>() });
        confirmResponse.EnsureSuccessStatusCode();
        var confirmResult = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>();

        var importedRows = confirmResult.GetProperty("importedRows").GetInt32();
        var autoCategorized = confirmResult.GetProperty("autoCategorized").GetInt32();
        var manualCount = confirmResult.GetProperty("manualCount").GetInt32();
        var confirmErrors = confirmResult.GetProperty("errorRows").GetInt32();
        var status = confirmResult.GetProperty("status").GetString();

        _output.WriteLine(
            $"Confirm result: imported={importedRows}, auto={autoCategorized}, " +
            $"manual={manualCount}, errors={confirmErrors}, status={status}");

        status.Should().Be("Completed");
        confirmErrors.Should().Be(0, "no rows should have errors");
        importedRows.Should().Be(200, "all 200 rows should be imported");

        // 8. Verify auto-categorization ≥ 80%
        var categorizedTotal = autoCategorized + manualCount;
        categorizedTotal.Should().BeGreaterThan(0);
        var coveragePercent = (double)autoCategorized / categorizedTotal * 100;
        _output.WriteLine($"Categorization coverage: {coveragePercent:F1}%");

        coveragePercent.Should().BeGreaterOrEqualTo(80.0,
            $"auto-categorization should cover ≥80% (was {coveragePercent:F1}%)");

        // 9. Check import batch history
        var batchesResponse = await _client.GetAsync("/api/financial/imports");
        batchesResponse.EnsureSuccessStatusCode();
        var batches = await batchesResponse.Content.ReadFromJsonAsync<JsonElement>();
        var batchArray = batches.EnumerateArray().ToArray();
        batchArray.Should().Contain(b => b.GetProperty("id").GetString() == batchId);
        var batchItem = batchArray.First(b => b.GetProperty("id").GetString() == batchId);
        batchItem.GetProperty("status").GetString().Should().Be("Completed");
        batchItem.GetProperty("importedRows").GetInt32().Should().Be(200);

        // 10. Verify specific transactions exist — paginate via cursor
        // because the endpoint caps pageSize at 100 (MaxPageSize).
        var allItems = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var url = $"/api/financial/transactions?dateFrom=2025-09-01&dateTo=2026-04-30&pageSize=100";
            if (cursor is not null) url += $"&cursor={Uri.EscapeDataString(cursor)}";
            var pageResponse = await _client.GetAsync(url);
            pageResponse.EnsureSuccessStatusCode();
            var pageResult = await pageResponse.Content.ReadFromJsonAsync<JsonElement>();
            allItems.AddRange(pageResult.GetProperty("items").EnumerateArray());
            cursor = pageResult.TryGetProperty("nextCursor", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
        } while (cursor is not null);
        var items = allItems.ToArray();

        _output.WriteLine($"Total transactions fetched: {items.Length}");
        items.Should().HaveCountGreaterOrEqualTo(200, "at least all imported rows must be present");

        // 11. Check for specific known transactions
        var descriptions = items
            .Select(i => i.GetProperty("description").GetString())
            .Where(d => d is not null)
            .ToList();

        descriptions.Should().Contain(d => d!.Contains("CONTINENTE BOM DIA"));
        descriptions.Should().Contain(d => d!.Contains("MARLOCONSULT"));
        descriptions.Should().Contain(d => d!.Contains("VIAVERDE"));
        descriptions.Should().Contain(d => d!.Contains("PASTELARIA ALOMA"));
        descriptions.Should().Contain(d => d!.Contains("VENCIMENTO"));

        // 12. Test duplicate detection (re-upload same file)
        await using var fileStream2 = File.OpenRead(_csvFilePath);
        using var formData2 = new MultipartFormDataContent();
        var fileContent2 = new StreamContent(fileStream2);
        fileContent2.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        formData2.Add(fileContent2, "file", "example-activo-bank.csv");

        var upload2Response = await _client.PostAsync(
            $"/api/financial/imports/upload?importProfileId={profileId}&accountId={accountId}",
            formData2);
        upload2Response.EnsureSuccessStatusCode();
        var upload2Result = await upload2Response.Content.ReadFromJsonAsync<JsonElement>();
        var preview2Rows = upload2Result.GetProperty("previewRows").EnumerateArray().ToArray();

        var duplicateCount = preview2Rows.Count(r =>
            r.GetProperty("isDuplicate").GetBoolean());
        _output.WriteLine($"Re-upload duplicate count: {duplicateCount}");

        duplicateCount.Should().Be(200,
            "all 200 rows should be detected as duplicates on re-upload");
    }

    [Fact]
    public async Task CsvImport_MultiTenancy_Isolation()
    {
        // Create two tenants
        var (clientA, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(
            _fixture, "csvmt-a");
        var (clientB, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(
            _fixture, "csvmt-b");

        // Tenant A: create profile and upload
        var profileResponse = await clientA.PostAsJsonAsync("/api/financial/import-profiles", new
        {
            name = "Test Profile A",
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

        var profileA = await profileResponse.Content.ReadFromJsonAsync<JsonElement>();

        // Tenant B: list profiles (should not see A's)
        var listB = await clientB.GetAsync("/api/financial/import-profiles");
        listB.EnsureSuccessStatusCode();
        var profilesB = await listB.Content.ReadFromJsonAsync<JsonElement>();
        var countB = profilesB.EnumerateArray().Count();

        // Tenant B should get 0 (own profiles) or 1 (seeded default),
        // but NOT Tenant A's profile
        var profileAId = profileA.GetProperty("id").GetString();

        // Tenant B tries to GET A's profile directly (should 404)
        var getAByB = await clientB.GetAsync($"/api/financial/import-profiles/{profileAId}");

        getAByB.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound,
            "Tenant B must not see Tenant A's import profile (anti-enumeration)");

        _output.WriteLine(
            $"Multi-tenancy OK: Tenant B has {countB} profiles, cannot read A's profile {profileAId}");
    }
}
