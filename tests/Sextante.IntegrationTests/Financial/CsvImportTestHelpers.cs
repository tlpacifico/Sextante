using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 grupo 7 — helpers partilhados pelos testes do import CSV:
/// perfil ActivoBank, upload com conta de destino, confirm e leituras.
/// </summary>
internal static class CsvImportTestHelpers
{
    public const string ActivoHeader = "Data Lanc.;Data Valor;Descrição;Valor;Saldo";

    public static async Task<Guid> CreateAccountAsync(
        HttpClient client,
        string name,
        short type = 0,
        string currency = "EUR",
        decimal openingBalance = 0m,
        DateOnly? openingBalanceDate = null)
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name,
            type,
            currency,
            openingBalanceAmount = openingBalance,
            openingBalanceDate = (openingBalanceDate ?? new DateOnly(2020, 1, 1)).ToString("yyyy-MM-dd"),
        });
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateCategoryAsync(HttpClient client, string name, int kind)
    {
        var response = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name,
            kind,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateCategoryRuleAsync(HttpClient client, string pattern, Guid categoryId, int priority)
    {
        var response = await client.PostAsJsonAsync("/api/financial/categorization-rules", new
        {
            name = pattern,
            pattern,
            matchType = "Contains",
            categoryId,
            priority,
        });
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateTransferRuleAsync(HttpClient client, string pattern, Guid targetAccountId, int priority)
    {
        var response = await client.PostAsJsonAsync("/api/financial/categorization-rules", new
        {
            name = pattern,
            pattern,
            matchType = "Contains",
            categoryId = (Guid?)null,
            priority,
            action = "MarkAsTransfer",
            targetAccountId,
        });
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Perfil do extrato ActivoBank: Data Valor / Descrição / Valor, dd/MM/yyyy, decimal vírgula.</summary>
    public static async Task<Guid> CreateActivoProfileAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/financial/import-profiles", new
        {
            name = $"ActivoBank {Guid.NewGuid():N}",
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
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static Task<HttpResponseMessage> PostUploadAsync(
        HttpClient client, string csv, Guid? accountId, Guid? profileId, string fileName = "extrato.csv")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", fileName);

        var query = new List<string>();
        if (accountId is not null) query.Add($"accountId={accountId}");
        if (profileId is not null) query.Add($"importProfileId={profileId}");
        var url = "/api/financial/imports/upload" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
        return client.PostAsync(url, form);
    }

    public static async Task<JsonElement> UploadAsync(HttpClient client, string csv, Guid accountId, Guid? profileId)
    {
        var response = await PostUploadAsync(client, csv, accountId, profileId);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<JsonElement> ConfirmAsync(
        HttpClient client, JsonElement upload, bool includeBeforeOpeningBalance = false, IEnumerable<Guid>? includeDuplicates = null)
    {
        var batchId = upload.GetProperty("batchId").GetString();
        var response = await client.PostAsJsonAsync($"/api/financial/imports/{batchId}/confirm", new
        {
            includeDuplicates = (includeDuplicates ?? []).ToArray(),
            includeBeforeOpeningBalance,
        });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<JsonElement> ImportAsync(
        HttpClient client, string csv, Guid accountId, Guid? profileId, bool includeBeforeOpeningBalance = false)
        => await ConfirmAsync(client, await UploadAsync(client, csv, accountId, profileId), includeBeforeOpeningBalance);

    public static IReadOnlyList<JsonElement> PreviewRows(JsonElement upload)
        => upload.GetProperty("previewRows").EnumerateArray().ToList();

    public static async Task<List<JsonElement>> ListTransactionsAsync(HttpClient client, Guid? accountId = null, string? kind = null)
    {
        var url = "/api/financial/transactions?pageSize=100&dateFrom=2000-01-01";
        if (accountId is not null) url += $"&accountIds={accountId}";
        if (kind is not null) url += $"&kind={kind}";
        var page = await client.GetFromJsonAsync<JsonElement>(url);
        return page.GetProperty("items").EnumerateArray().ToList();
    }

    public static async Task<decimal> CurrentBalanceAsync(HttpClient client, Guid accountId)
    {
        var account = await client.GetFromJsonAsync<JsonElement>($"/api/financial/accounts/{accountId}");
        return account.GetProperty("currentBalance").GetProperty("amount").GetDecimal();
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} falhou " +
                $"({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }
    }
}
