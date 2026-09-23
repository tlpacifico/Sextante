using System.Globalization;
using System.Text.Json;
using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.ImportBatches;
using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.PublicApi.Events;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.CsvImport;

[NonTransactional]
public static class CsvImportHandlers
{
    public static async Task<UploadCsvResponse> Handle(
        Stream csvStream,
        string fileName,
        Guid? importProfileId,
        IImportProfileRepository profileRepo,
        IImportBatchRepository batchRepo,
        IDuplicateDetector duplicateDetector,
        ICategorizationRuleEngine ruleEngine,
        ICsvParser csvParser,
        ITenantContext tenant,
        CancellationToken ct)
    {
        ImportProfile? profile = null;
        if (importProfileId is not null)
            profile = await profileRepo.GetByIdAsync(importProfileId.Value, ct);

        var options = new CsvParseOptions(
            Delimiter: profile?.Delimiter,
            HasHeaderRow: profile?.HasHeaderRow ?? true,
            SkipRows: profile?.SkipRows ?? 0,
            MaxPreviewRows: 1000);

        var parseResult = csvParser.Parse(csvStream, options, ct);

        var batch = ImportBatch.StartParsing(fileName, tenant.TenantId, importProfileId);

        var resolver = new CsvColumnResolver(parseResult.Headers, profile);

        var errors = new List<string>();
        var previewRowsResponse = new List<PreviewRowDto>();

        for (int i = 0; i < parseResult.Rows.Count; i++)
        {
            var row = parseResult.Rows[i];
            var rawValues = row.ToList();

            // Extract date
            DateOnly? date = null;
            string? dateError = null;
            if (resolver.TryGetDate(rawValues, out var dateStr))
            {
                if (!TryParseDate(dateStr, profile?.DateFormat, out var d))
                    dateError = $"Data inválida: '{dateStr}'";
                else
                    date = d;
            }

            // Extract amount
            decimal? amount = null;
            string? amountError = null;
            if (resolver.TryGetAmount(rawValues, profile?.DecimalSeparator, out var parsedAmount))
                amount = parsedAmount;

            // Extract description
            var description = resolver.TryGetDescription(rawValues, out var desc) ? desc : null;

            // Extract currency
            var currency = resolver.TryGetCurrency(rawValues, out var cur) ? cur : null;

            // Extract account name
            var accountName = resolver.TryGetAccount(rawValues, out var acc) ? acc : null;

            // Extract credit/debit indicator
            var indicator = resolver.TryGetCreditDebitIndicator(rawValues, out var ind) ? ind : null;

            var error = dateError ?? amountError;
            if (error is not null)
                errors.Add($"Linha {i + 1}: {error}");

            previewRowsResponse.Add(new PreviewRowDto(
                i,
                rawValues,
                false,
                null,
                null,
                null,
                false,
                error));
        }

        // Detect duplicates — build a parsed-tx list keyed by the preview row index
        // so the detector can return per-row matches we can correlate back.
        var parsedTx = new List<ParsedTransaction>();
        for (int i = 0; i < previewRowsResponse.Count; i++)
        {
            if (previewRowsResponse[i].Error is not null) continue;
            var vals = previewRowsResponse[i].Values.ToList();
            if (!resolver.TryGetDate(vals, out var dStr) || !TryParseDate(dStr, profile?.DateFormat, out var d))
                continue;
            if (!resolver.TryGetAmount(vals, profile?.DecimalSeparator, out var amt) || amt == 0)
                continue;
            var curr = resolver.TryGetCurrency(vals, out var c) ? c : "EUR";
            var desc = resolver.TryGetDescription(vals, out var descr) ? descr : string.Empty;
            // Storage stores Math.Abs(amount) (sign carried by category kind),
            // so duplicate detection must compare on the absolute value too.
            parsedTx.Add(new ParsedTransaction(i, d, Math.Abs(amt), curr, desc));
        }

        var duplicateMatches = await duplicateDetector.FindPotentialDuplicatesAsync(parsedTx, tenant.TenantId, ct);
        foreach (var match in duplicateMatches)
        {
            if (match.RowIndex < 0 || match.RowIndex >= previewRowsResponse.Count) continue;
            var p = previewRowsResponse[match.RowIndex];
            previewRowsResponse[match.RowIndex] = new PreviewRowDto(
                p.RowIndex,
                p.Values,
                true,
                match.ExistingTransactionId,
                p.SuggestedCategoryName,
                p.SuggestedCategoryId,
                p.IsAutoCategorized,
                p.Error);
        }

        // Apply categorization rules — pair each candidate with its preview row index
        // so results bind back unambiguously even when some rows have blank descriptions.
        var ruleCandidates = new List<(int RowIndex, TransactionToCategorize Tx)>();
        for (int i = 0; i < previewRowsResponse.Count; i++)
        {
            if (previewRowsResponse[i].Error is not null) continue;
            var vals = previewRowsResponse[i].Values.ToList();
            if (!resolver.TryGetDescription(vals, out var desc) || string.IsNullOrWhiteSpace(desc)) continue;
            ruleCandidates.Add((i, new TransactionToCategorize(Guid.CreateVersion7(), desc)));
        }

        var ruleResults = await ruleEngine.ApplyAsync(
            ruleCandidates.Select(c => c.Tx).ToList(),
            tenant.TenantId,
            ct);

        for (int k = 0; k < ruleCandidates.Count && k < ruleResults.Count; k++)
        {
            var result = ruleResults[k];
            if (result.NewCategoryId is null) continue;
            var rowIdx = ruleCandidates[k].RowIndex;
            var p = previewRowsResponse[rowIdx];
            previewRowsResponse[rowIdx] = new PreviewRowDto(
                p.RowIndex, p.Values, p.IsDuplicate, p.DuplicateTransactionId,
                "—", result.NewCategoryId, true, p.Error);
        }

        var wrapper = new PreviewWrapper(parseResult.Headers.ToList(), previewRowsResponse);
        var previewJson = JsonSerializer.Serialize(wrapper);
        batch.SetPreview(
            parseResult.TotalRowCount,
            previewJson,
            parseResult.Truncated,
            previewRowsResponse.Count(r => r.Error is not null));

        await batchRepo.AddAsync(batch, ct);
        await batchRepo.SaveChangesAsync(ct);

        return new UploadCsvResponse(
            batch.Id,
            parseResult.Headers,
            previewRowsResponse,
            parseResult.TotalRowCount,
            parseResult.Truncated,
            profile?.Delimiter ?? ";",
            profile?.HasHeaderRow ?? true,
            errors);
    }

    public static async Task<UploadCsvResponse?> Handle(
        UpdatePreviewCommand command,
        IImportBatchRepository batchRepo,
        ICsvParser csvParser,
        ICategorizationRuleEngine ruleEngine,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var batch = await batchRepo.GetByIdAsync(command.BatchId, ct);
        if (batch is null) return null;

        var previewWrapper = JsonSerializer.Deserialize<PreviewWrapper>(batch.ParsedPreviewJson);
        var previewRows = previewWrapper?.Rows?.ToList() ?? new List<PreviewRowDto>();
        var storedHeaders = previewWrapper?.Headers ?? new List<string>();

        var resolver = new CsvColumnResolver(storedHeaders, command.ColumnMappings);

        var ruleCandidates = new List<(int Index, TransactionToCategorize Tx)>();
        for (int i = 0; i < previewRows.Count; i++)
        {
            if (previewRows[i].Error is not null) continue;
            var vals = previewRows[i].Values.ToList();
            if (!resolver.TryGetDescription(vals, out var desc) || string.IsNullOrWhiteSpace(desc)) continue;
            ruleCandidates.Add((i, new TransactionToCategorize(Guid.CreateVersion7(), desc)));
        }

        var ruleResults = await ruleEngine.ApplyAsync(
            ruleCandidates.Select(c => c.Tx).ToList(),
            tenant.TenantId,
            ct);

        for (int k = 0; k < ruleCandidates.Count && k < ruleResults.Count; k++)
        {
            var result = ruleResults[k];
            if (result.NewCategoryId is null) continue;
            var rowIdx = ruleCandidates[k].Index;
            var preview = previewRows[rowIdx];
            previewRows[rowIdx] = new PreviewRowDto(
                preview.RowIndex,
                preview.Values,
                preview.IsDuplicate,
                preview.DuplicateTransactionId,
                "—",
                result.NewCategoryId,
                true,
                preview.Error);
        }

        var updWrapper = new PreviewWrapper(storedHeaders, previewRows);
        var previewJson = JsonSerializer.Serialize(updWrapper);
        batch.ParsedPreviewJson = previewJson;
        batchRepo.Update(batch);
        await batchRepo.SaveChangesAsync(ct);

        return new UploadCsvResponse(
            batch.Id,
            new List<string>(),
            previewRows,
            batch.TotalRows,
            batch.PreviewTruncated,
            ";",
            true,
            new List<string>());
    }

    public static async Task<ImportConfirmResponse> Handle(
        ConfirmImportCommand command,
        IImportBatchRepository batchRepo,
        IImportProfileRepository profileRepo,
        IAccountRepository accountRepo,
        ITransactionRepository txRepo,
        ICategoryRepository categoryRepo,
        ICategorizationRuleEngine ruleEngine,
        IExchangeRateService exchangeRateService,
        ITenantCurrencyResolver currencyResolver,
        IIntegrationEventPublisher events,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var batch = await batchRepo.GetByIdAsync(command.BatchId, ct);
        if (batch is null)
            throw new InvalidOperationException("Lote de importação não encontrado.");

        var primaryCurrency = await currencyResolver.GetPrimaryCurrencyAsync(ct);
        var created = new List<Transaction>();

        batch.MarkImporting();
        batchRepo.Update(batch);

        var previewWrapper = JsonSerializer.Deserialize<PreviewWrapper>(batch.ParsedPreviewJson);
        var previewRows = previewWrapper?.Rows ?? new List<PreviewRowDto>();
        var csvHeaders = previewWrapper?.Headers ?? new List<string>();

        // Load profile for column mappings
        ImportProfile? profile = null;
        if (batch.ImportProfileId is not null)
            profile = await profileRepo.GetByIdAsync(batch.ImportProfileId.Value, ct);

        // Build column resolver from profile or auto-detect
        var resolver = new CsvColumnResolver(csvHeaders, profile);

        // Pre-load accounts for lookup
        var accounts = await accountRepo.ListAsync(ct);
        var categories = await categoryRepo.ListAsync(ct);

        var includeSet = new HashSet<Guid>(command.IncludeDuplicates);
        var rowsToImport = previewRows
            .Where(r => r.Error is null && (!r.IsDuplicate || includeSet.Contains(r.DuplicateTransactionId ?? Guid.Empty)))
            .ToList();

        var autoCategorized = 0;
        var manualCount = 0;
        var errorRows = 0;
        var imported = 0;

        foreach (var row in rowsToImport)
        {
            try
            {
                var values = row.Values.ToList();

                // Resolve account: by mapped column or default
                Account? account = null;
                if (resolver.TryGetAccount(values, out var accountName) && !string.IsNullOrWhiteSpace(accountName))
                {
                    account = accounts.FirstOrDefault(a =>
                        string.Equals(a.Name.Trim(), accountName.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (account is null)
                        throw new AccountNotFoundForImportException(accountName);
                }
                else
                {
                    account = accounts.FirstOrDefault();
                    if (account is null)
                        throw new AccountNotFoundForImportException("default");
                }

                // Parse date using profile's DateFormat
                DateOnly date;
                if (!resolver.TryGetDate(values, out var dateStr) || !TryParseDate(dateStr, profile?.DateFormat, out date))
                {
                    errorRows++;
                    continue;
                }

                // Parse amount using profile's DecimalSeparator
                if (!resolver.TryGetAmount(values, profile?.DecimalSeparator, out var parsedAmount) || parsedAmount == 0)
                {
                    errorRows++;
                    continue;
                }

                // Check credit/debit indicator
                var indicator = resolver.TryGetCreditDebitIndicator(values, out var ind) ? ind?.Trim().ToUpperInvariant() : null;
                var finalAmount = parsedAmount;
                if (indicator == "DEBIT" || indicator == "D")
                    finalAmount = -Math.Abs(parsedAmount);

                // Make positive for Transaction.Create (it requires positive); mark as expense/income via category
                var absAmount = Math.Abs(finalAmount);

                // Determine currency from mapping or account
                var currency = account.Currency;
                if (resolver.TryGetCurrency(values, out var mappedCurrency) && !string.IsNullOrWhiteSpace(mappedCurrency))
                    currency = mappedCurrency;

                // Get description
                var description = resolver.TryGetDescription(values, out var desc) ? desc : null;

                // Resolve exchange rate if needed
                ExchangeRateSnapshot? exchangeRate = null;
                if (!string.Equals(currency, primaryCurrency, StringComparison.Ordinal))
                {
                    try
                    {
                        var resolved = await exchangeRateService.ResolveAsync(
                            currency, primaryCurrency, new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero), ct);
                        if (resolved is not null)
                            exchangeRate = resolved;
                    }
                    catch
                    {
                        // Exchange rate unavailable - skip with error
                        errorRows++;
                        continue;
                    }
                }

                var money = new Money(absAmount, currency);

                // Category: use suggested (from preview) or infer from amount sign
                var categoryId = row.SuggestedCategoryId;
                if (categoryId is null)
                {
                    // Phase 5.5 — inferir tipo pelo sinal do valor:
                    // parsedAmount > 0 → receita; parsedAmount < 0 → despesa.
                    // Credit/debit indicator já é considerado acima via finalAmount.
                    var isIncome = finalAmount > 0;
                    var firstMatchingKind = categories.FirstOrDefault(c =>
                        c.Kind == (isIncome ? CategoryKind.Income : CategoryKind.Expense));
                    if (firstMatchingKind is not null)
                    {
                        categoryId = firstMatchingKind.Id;
                        manualCount++;
                    }
                    else
                    {
                        errorRows++;
                        continue;
                    }
                }
                else
                {
                    autoCategorized++;
                }

                var tx = Transaction.Create(
                    account.Id,
                    categoryId.Value,
                    new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero),
                    money,
                    description,
                    null,
                    tenant.TenantId,
                    exchangeRate);

                // Run rule engine for audit trail
                var toCategorize = new List<TransactionToCategorize> { new(tx.Id, description ?? string.Empty) };
                var ruleResults = await ruleEngine.ApplyAsync(toCategorize, tenant.TenantId, ct);
                var ruleResult = ruleResults.FirstOrDefault();
                if (ruleResult?.MatchedRuleId is not null)
                {
                    tx.MarkCategorizedByRule(ruleResult.MatchedRuleId.Value);
                    if (ruleResult.NewCategoryId is not null)
                        tx.SetCategory(ruleResult.NewCategoryId.Value);
                }

                await txRepo.AddAsync(tx, ct);
                created.Add(tx);
                imported++;
            }
            catch (FinancialDomainException)
            {
                errorRows++;
            }
            catch
            {
                errorRows++;
            }
        }

        await txRepo.SaveChangesAsync(ct);

        batch.Complete(imported, autoCategorized, manualCount);
        batchRepo.Update(batch);
        await batchRepo.SaveChangesAsync(ct);

        // Phase 6.5 §0.5 — orçamentos e alertas (Phase 5b) só reagem a
        // eventos; o import tem de os publicar como qualquer outra criação.
        foreach (var tx in created)
        {
            await events.PublishAsync(
                new TransactionCreatedIntegrationEvent(
                    tx.Id,
                    tenant.TenantId.Value,
                    tx.AccountId,
                    tx.CategoryId,
                    tx.Amount.Amount,
                    tx.Amount.Currency,
                    tx.OccurredAt,
                    DateTimeOffset.UtcNow),
                ct);
        }

        return new ImportConfirmResponse(
            batch.Id,
            imported,
            autoCategorized,
            manualCount,
            errorRows,
            batch.Status.ToString());
    }

    public static async Task<IReadOnlyList<ImportBatchResponse>> Handle(
        ListImportBatchesQuery query,
        IImportBatchRepository batchRepo,
        CancellationToken ct)
    {
        var batches = await batchRepo.ListAsync(ct);
        return batches.Select(b => new ImportBatchResponse(
            b.Id,
            b.ImportProfileId,
            b.FileName,
            b.Status.ToString(),
            b.TotalRows,
            b.ImportedRows,
            b.DuplicateRows,
            b.ErrorRows,
            b.CreatedAt.ToString("O"))).ToList();
    }

    // --- Parsing helpers ---

    private static bool TryParseDate(string? dateStr, string? dateFormat, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        var format = dateFormat ?? "dd-MM-yyyy";

        if (DateOnly.TryParseExact(dateStr.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (DateOnly.TryParse(dateStr.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (DateOnly.TryParse(dateStr.Trim(), new CultureInfo("pt-PT"), DateTimeStyles.None, out date))
            return true;

        return false;
    }

}

/// <summary>
/// Resolves CSV column index → TransactionField based on ImportProfile
/// ColumnMappings or auto-detection heuristics.
/// </summary>
public sealed class CsvColumnResolver
{
    private readonly Dictionary<TransactionField, int> _map = new();

    public int DescriptionIndex => _map.TryGetValue(TransactionField.Description, out var i) ? i : 2;

    public CsvColumnResolver(IReadOnlyList<string> headers, IReadOnlyList<ColumnMappingInput> mappings)
    {
        foreach (var m in mappings)
        {
            if (string.IsNullOrWhiteSpace(m.TransactionField)) continue;
            if (!Enum.TryParse<TransactionField>(m.TransactionField, ignoreCase: true, out var field)) continue;
            var index = FindColumnIndex(headers, m.CsvColumnName);
            if (index >= 0)
                _map[field] = index;
        }
    }

    public CsvColumnResolver(IReadOnlyList<string> headers, ImportProfile? profile)
    {
        if (profile is not null && profile.ColumnMappings.Count > 0)
        {
            foreach (var mapping in profile.ColumnMappings)
            {
                var index = FindColumnIndex(headers, mapping.CsvColumnName);
                if (index >= 0)
                    _map[mapping.TransactionField] = index;
            }
            return;
        }

        // Auto-detection heuristics
        for (int i = 0; i < headers.Count; i++)
        {
            var h = headers[i].Trim().ToLowerInvariant();
            if (h.Contains("data") || h.Contains("date"))
                _map.TryAdd(TransactionField.Date, i);
            else if (h.Contains("valor") || h.Contains("amount") || h.Contains("montante"))
                _map.TryAdd(TransactionField.Amount, i);
            else if (h.Contains("descri"))
                _map.TryAdd(TransactionField.Description, i);
            else if (h.Contains("moeda") || h.Contains("currency"))
                _map.TryAdd(TransactionField.Currency, i);
            else if (h.Contains("conta") || h.Contains("account"))
                _map.TryAdd(TransactionField.Account, i);
            else if (h.Contains("débito") || h.Contains("debito") || h.Contains("crédito") || h.Contains("credito")
                     || h.Contains("debit") || h.Contains("credit") || h.Contains("dc"))
                _map.TryAdd(TransactionField.CreditDebitIndicator, i);
        }

        // Fallback defaults
        if (!_map.ContainsKey(TransactionField.Date) && headers.Count > 0)
            _map[TransactionField.Date] = 0;
        if (!_map.ContainsKey(TransactionField.Amount) && headers.Count > 3)
            _map[TransactionField.Amount] = 3; // common PT CSV position
        if (!_map.ContainsKey(TransactionField.Description) && headers.Count > 2)
            _map[TransactionField.Description] = 2;
    }

    public bool TryGetDate(List<string> values, out string dateStr)
    {
        dateStr = string.Empty;
        if (!_map.TryGetValue(TransactionField.Date, out var i) || i >= values.Count)
            return false;
        dateStr = values[i];
        return !string.IsNullOrWhiteSpace(dateStr);
    }

    public bool TryGetAmount(List<string> values, string? decimalSeparator, out decimal amount)
    {
        amount = 0;
        if (!_map.TryGetValue(TransactionField.Amount, out var i) || i >= values.Count)
            return false;

        var raw = values[i];
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var cleaned = raw.Trim().Replace(" ", "").Replace("\u00A0", "");

        // Handle comma as decimal separator (PT format)
        if (decimalSeparator == "," || cleaned.Contains(',') && !cleaned.Contains('.'))
        {
            cleaned = cleaned.Replace(",", ".");
        }

        // Remove currency symbols
        cleaned = cleaned.TrimStart('€', '$', '£', 'R', '+', '-');

        // Determine sign
        var isNegative = raw.Trim().StartsWith('-');
        cleaned = cleaned.TrimStart('-');

        if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out amount))
        {
            if (isNegative) amount = -amount;
            return true;
        }

        return false;
    }

    public bool TryGetDescription(List<string> values, out string description)
    {
        description = string.Empty;
        if (!_map.TryGetValue(TransactionField.Description, out var i) || i >= values.Count)
            return false;
        description = values[i];
        return !string.IsNullOrWhiteSpace(description);
    }

    public bool TryGetCurrency(List<string> values, out string currency)
    {
        currency = string.Empty;
        if (!_map.TryGetValue(TransactionField.Currency, out var i) || i >= values.Count)
            return false;
        currency = values[i]?.Trim().ToUpperInvariant() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(currency) && currency.Length == 3;
    }

    public bool TryGetAccount(List<string> values, out string accountName)
    {
        accountName = string.Empty;
        if (!_map.TryGetValue(TransactionField.Account, out var i) || i >= values.Count)
            return false;
        accountName = values[i];
        return !string.IsNullOrWhiteSpace(accountName);
    }

    public bool TryGetCreditDebitIndicator(List<string> values, out string indicator)
    {
        indicator = string.Empty;
        if (!_map.TryGetValue(TransactionField.CreditDebitIndicator, out var i) || i >= values.Count)
            return false;
        indicator = values[i];
        return !string.IsNullOrWhiteSpace(indicator);
    }

    private static int FindColumnIndex(IReadOnlyList<string> headers, string columnName)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i].Trim(), columnName.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}

public static class DictionaryExtensions
{
    public static void TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value)
        where TKey : notnull
    {
        if (!dict.ContainsKey(key))
            dict[key] = value;
    }
}

public sealed record PreviewWrapper(IReadOnlyList<string> Headers, IReadOnlyList<PreviewRowDto> Rows);

public sealed record ImportConfirmResponse(
    Guid BatchId,
    int ImportedRows,
    int AutoCategorized,
    int ManualCount,
    int ErrorRows,
    string Status);
