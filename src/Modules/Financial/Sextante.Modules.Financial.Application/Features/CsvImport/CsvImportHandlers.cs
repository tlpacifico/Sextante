using System.Globalization;
using System.Text.Json;
using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Application.Features.Transfers;
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
        Guid? accountId,
        IImportProfileRepository profileRepo,
        IImportBatchRepository batchRepo,
        IAccountRepository accountRepo,
        ICategoryRepository categoryRepo,
        IDuplicateDetector duplicateDetector,
        ICategorizationRuleEngine ruleEngine,
        ITransferCounterpartQuery transferQuery,
        ICsvParser csvParser,
        ITenantContext tenant,
        CancellationToken ct)
    {
        // Phase 6.5 grupo 7 (Q2) — a conta de destino é obrigatória; conta de
        // outro tenant não existe para este (filtro global), mesma resposta.
        if (accountId is null)
            throw new ImportAccountRequiredException();
        var account = await accountRepo.GetByIdAsync(accountId.Value, ct)
            ?? throw new ImportAccountRequiredException();

        ImportProfile? profile = null;
        if (importProfileId is not null)
            profile = await profileRepo.GetByIdAsync(importProfileId.Value, ct);

        var options = new CsvParseOptions(
            Delimiter: profile?.Delimiter,
            HasHeaderRow: profile?.HasHeaderRow ?? true,
            SkipRows: profile?.SkipRows ?? 0,
            MaxPreviewRows: 1000);

        var parseResult = csvParser.Parse(csvStream, options, ct);

        var batch = ImportBatch.StartParsing(fileName, tenant.TenantId, importProfileId, account.Id);

        var settings = new ImportParseSettings(
            profile is { ColumnMappings.Count: > 0 }
                ? profile.ColumnMappings
                    .Select(m => new ColumnMappingInput(m.CsvColumnName, m.TransactionField.ToString()))
                    .ToList()
                : null,
            profile?.DateFormat,
            profile?.DecimalSeparator);
        var headers = parseResult.Headers.ToList();
        var resolver = ImportRowParser.ResolverFor(headers, settings, profile);

        var rows = await ImportPreviewBuilder.BuildAsync(
            parseResult.Rows.Select(r => (IReadOnlyList<string>)r.ToList()).ToList(),
            resolver,
            settings,
            account,
            await accountRepo.ListAsync(ct),
            await categoryRepo.ListAsync(ct),
            duplicateDetector,
            ruleEngine,
            transferQuery,
            tenant.TenantId,
            ct);

        var previewJson = JsonSerializer.Serialize(new PreviewWrapper(headers, rows, settings));
        batch.SetPreview(
            parseResult.TotalRowCount,
            previewJson,
            parseResult.Truncated,
            rows.Count(r => r.Error is not null));

        await batchRepo.AddAsync(batch, ct);
        await batchRepo.SaveChangesAsync(ct);

        return new UploadCsvResponse(
            batch.Id,
            parseResult.Headers,
            rows,
            parseResult.TotalRowCount,
            parseResult.Truncated,
            profile?.Delimiter ?? ";",
            profile?.HasHeaderRow ?? true,
            RowErrors(rows));
    }

    public static async Task<UploadCsvResponse?> Handle(
        UpdatePreviewCommand command,
        IImportBatchRepository batchRepo,
        IAccountRepository accountRepo,
        ICategoryRepository categoryRepo,
        IDuplicateDetector duplicateDetector,
        ICategorizationRuleEngine ruleEngine,
        ITransferCounterpartQuery transferQuery,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var batch = await batchRepo.GetByIdAsync(command.BatchId, ct);
        if (batch is null) return null;

        var account = await GetBatchAccountAsync(batch, accountRepo, ct);

        var previewWrapper = JsonSerializer.Deserialize<PreviewWrapper>(batch.ParsedPreviewJson);
        var storedRows = previewWrapper?.Rows ?? new List<PreviewRowDto>();
        var storedHeaders = previewWrapper?.Headers ?? new List<string>();

        // Q3 — as definições escolhidas no passo 2 passam a ser as do lote.
        var settings = new ImportParseSettings(command.ColumnMappings, command.DateFormat, command.DecimalSeparator);
        var resolver = ImportRowParser.ResolverFor(storedHeaders, settings, profile: null);

        var rows = await ImportPreviewBuilder.BuildAsync(
            storedRows.Select(r => r.Values).ToList(),
            resolver,
            settings,
            account,
            await accountRepo.ListAsync(ct),
            await categoryRepo.ListAsync(ct),
            duplicateDetector,
            ruleEngine,
            transferQuery,
            tenant.TenantId,
            ct);

        batch.ParsedPreviewJson = JsonSerializer.Serialize(new PreviewWrapper(storedHeaders, rows, settings));
        batchRepo.Update(batch);
        await batchRepo.SaveChangesAsync(ct);

        return new UploadCsvResponse(
            batch.Id,
            storedHeaders,
            rows,
            batch.TotalRows,
            batch.PreviewTruncated,
            ";",
            true,
            RowErrors(rows));
    }

    public static async Task<ImportConfirmResponse> Handle(
        ConfirmImportCommand command,
        IImportBatchRepository batchRepo,
        IImportProfileRepository profileRepo,
        IAccountRepository accountRepo,
        ITransactionRepository txRepo,
        ICategoryRepository categoryRepo,
        ICategorizationRuleEngine ruleEngine,
        ITransferCounterpartQuery transferQuery,
        IExchangeRateService exchangeRateService,
        ITenantCurrencyResolver currencyResolver,
        IIntegrationEventPublisher events,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var batch = await batchRepo.GetByIdAsync(command.BatchId, ct);
        if (batch is null)
            throw new InvalidOperationException("Lote de importação não encontrado.");

        var batchAccount = await GetBatchAccountAsync(batch, accountRepo, ct);
        var primaryCurrency = await currencyResolver.GetPrimaryCurrencyAsync(ct);
        var created = new List<Transaction>();

        batch.MarkImporting();
        batchRepo.Update(batch);

        var previewWrapper = JsonSerializer.Deserialize<PreviewWrapper>(batch.ParsedPreviewJson);
        var previewRows = previewWrapper?.Rows ?? new List<PreviewRowDto>();
        var csvHeaders = previewWrapper?.Headers ?? new List<string>();

        ImportProfile? profile = null;
        if (batch.ImportProfileId is not null)
            profile = await profileRepo.GetByIdAsync(batch.ImportProfileId.Value, ct);

        // Q3 — mesmas definições do preview; lotes antigos sem definições
        // gravadas continuam com perfil/auto-deteção.
        var settings = previewWrapper?.Settings
            ?? new ImportParseSettings(null, profile?.DateFormat, profile?.DecimalSeparator);
        var resolver = ImportRowParser.ResolverFor(csvHeaders, previewWrapper?.Settings, profile);

        var accounts = await accountRepo.ListAsync(ct);
        var categories = await categoryRepo.ListAsync(ct);
        var categoriesById = categories.ToDictionary(c => c.Id);

        var accountsById = accounts.ToDictionary(a => a.Id);
        var includeSet = new HashSet<Guid>(command.IncludeDuplicates);

        var autoCategorized = 0;
        var manualCount = 0;
        var errorRows = 0;
        var skippedBeforeOpeningBalance = 0;
        var imported = 0;
        var transfersCreated = 0;
        var transfersLinked = 0;
        var transfersAlreadyRecorded = 0;
        var updated = new List<Transaction>();

        // Parse de novo com as definições do lote; a exclusão "antes do saldo
        // inicial" usa a OpeningBalanceDate atual da conta (R5). Linhas
        // "já registadas" (Q1) não se importam, mas corrigem a data da perna.
        var items = new List<ConfirmItem>();
        foreach (var previewRow in previewRows)
        {
            if (previewRow.Error is not null) continue;

            var forced = previewRow.IsDuplicate
                && includeSet.Contains(previewRow.DuplicateTransactionId ?? Guid.Empty);
            var alreadyRecorded = previewRow.IsDuplicate && !forced
                && previewRow.TransferStatus == nameof(ImportTransferStatus.AlreadyRecorded);
            if (previewRow.IsDuplicate && !forced && !alreadyRecorded) continue;

            var row = ImportRowParser.Parse(previewRow.Values, resolver, settings, out _);
            var account = row is null ? null : ImportRowParser.ResolveAccount(row, batchAccount, accounts, out _);
            if (row is null || account is null)
            {
                errorRows++;
                continue;
            }

            if (!alreadyRecorded && row.Date < account.OpeningBalanceDate && !command.IncludeBeforeOpeningBalance)
            {
                skippedBeforeOpeningBalance++;
                continue;
            }

            items.Add(new ConfirmItem(previewRow, row, account, forced, alreadyRecorded));
        }

        // Uma só passagem do motor de regras (R1): o resultado decide a
        // categoria ou a transferência; a direção vem sempre do sinal.
        var ruleInputs = items
            .Select(item => new TransactionToCategorize(Guid.CreateVersion7(), item.Row.Description ?? string.Empty))
            .ToList();
        var ruleResults = await ruleEngine.ApplyAsync(ruleInputs, tenant.TenantId, ct);
        var claimed = new HashSet<Guid>();

        for (var i = 0; i < items.Count; i++)
        {
            var (previewRow, row, account, forced, alreadyRecorded) = items[i];
            var ruleResult = i < ruleResults.Count ? ruleResults[i] : null;
            var currency = row.Currency ?? account.Currency;
            var occurredAt = new DateTimeOffset(row.Date, TimeOnly.MinValue, TimeSpan.Zero);

            ImportTransferResolution? resolution = null;
            Account? target = null;
            if (ruleResult?.TargetAccountId is { } targetId)
            {
                accountsById.TryGetValue(targetId, out target);
                resolution = await ImportTransferResolver.ResolveAsync(
                    account, target, row.Direction, row.AbsAmount, currency, row.Date, transferQuery, claimed, ct);
            }

            if (alreadyRecorded)
            {
                // Q1 — só se a perna continua a ser a mesma que o preview mostrou.
                if (resolution is { Status: ImportTransferStatus.AlreadyRecorded, TransactionId: { } legId }
                    && legId == previewRow.DuplicateTransactionId
                    && await txRepo.GetByIdAsync(legId, ct) is { } leg)
                {
                    if (DateOnly.FromDateTime(leg.OccurredAt.UtcDateTime) != row.Date)
                    {
                        leg.UpdateTransferLeg(leg.AccountId, occurredAt, leg.Amount, leg.Description);
                        txRepo.Update(leg);
                        updated.Add(leg);
                    }

                    transfersAlreadyRecorded++;
                }

                continue;
            }

            // Duplicado incluído à força: é uma transferência nova (Q1).
            if (forced && resolution?.Status == ImportTransferStatus.AlreadyRecorded)
            {
                resolution = target!.Currency == currency
                    ? new ImportTransferResolution(ImportTransferStatus.CreateCounterpart, null)
                    : new ImportTransferResolution(ImportTransferStatus.CurrencyMismatch, null);
            }

            ExchangeRateSnapshot? exchangeRate = null;
            if (!string.Equals(currency, primaryCurrency, StringComparison.Ordinal))
            {
                try
                {
                    exchangeRate = await exchangeRateService.ResolveAsync(currency, primaryCurrency, occurredAt, ct);
                }
                catch
                {
                    // Câmbio indisponível: linha com erro.
                    errorRows++;
                    continue;
                }
            }

            var money = new Money(row.AbsAmount, currency);

            try
            {
                if (resolution is { Status: ImportTransferStatus.CreateCounterpart or ImportTransferStatus.LinkExisting })
                {
                    // R4 — perna nesta conta + contraperna (nova ou existente), no mesmo SaveChanges.
                    var transferId = GuidV7.NewId();
                    var leg = Transaction.CreateTransferLeg(
                        account.Id, transferId, row.Direction, occurredAt, money, row.Description,
                        tenant.TenantId, exchangeRate);
                    leg.MarkCategorizedByRule(ruleResult!.MatchedRuleId!.Value);

                    if (resolution.Status == ImportTransferStatus.LinkExisting)
                    {
                        var counterpart = await txRepo.GetByIdAsync(resolution.TransactionId!.Value, ct)
                            ?? throw new TransferCounterpartAlreadyLinkedException();
                        counterpart.ConvertToTransferLeg(transferId);
                        txRepo.Update(counterpart);
                        updated.Add(counterpart);
                        transfersLinked++;
                    }
                    else
                    {
                        var opposite = row.Direction == TransactionDirection.Outflow
                            ? TransactionDirection.Inflow
                            : TransactionDirection.Outflow;
                        var counterpartLeg = Transaction.CreateTransferLeg(
                            target!.Id, transferId, opposite, occurredAt, new Money(row.AbsAmount, target.Currency),
                            row.Description, tenant.TenantId, exchangeRate);
                        await txRepo.AddAsync(counterpartLeg, ct);
                        created.Add(counterpartLeg);
                        transfersCreated++;
                    }

                    await txRepo.AddAsync(leg, ct);
                    created.Add(leg);
                    imported++;
                    continue;
                }

                Category? category = null;
                var fromRule = false;
                if (ruleResult?.NewCategoryId is { } ruleCategoryId
                    && categoriesById.TryGetValue(ruleCategoryId, out var ruleCategory)
                    && Transaction.DirectionFor(ruleCategory.Kind) == row.Direction)
                {
                    category = ruleCategory;
                    fromRule = true;
                }
                else
                {
                    // Phase 5.5 — sem regra aplicável: primeira categoria do tipo do sinal.
                    var kind = row.Direction == TransactionDirection.Inflow ? CategoryKind.Income : CategoryKind.Expense;
                    category = categories.FirstOrDefault(c => c.Kind == kind);
                }

                if (category is null)
                {
                    errorRows++;
                    continue;
                }

                var tx = Transaction.CreateRegular(
                    account.Id,
                    category.Id,
                    category.Kind,
                    occurredAt,
                    money,
                    row.Description,
                    null,
                    tenant.TenantId,
                    exchangeRate);

                if (fromRule)
                {
                    tx.MarkCategorizedByRule(ruleResult!.MatchedRuleId!.Value);
                    autoCategorized++;
                }
                else
                {
                    manualCount++;
                }

                await txRepo.AddAsync(tx, ct);
                created.Add(tx);
                imported++;
            }
            catch (FinancialDomainException)
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
        // Pernas de transferência vão com CategoryId nulo (D10).
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

        foreach (var tx in updated)
        {
            await events.PublishAsync(
                new TransactionUpdatedIntegrationEvent(
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
            batch.Status.ToString(),
            skippedBeforeOpeningBalance,
            transfersCreated,
            transfersLinked,
            transfersAlreadyRecorded);
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

    /// <summary>R10 — lotes anteriores ao grupo 7 não têm conta de destino.</summary>
    private static async Task<Account> GetBatchAccountAsync(
        ImportBatch batch, IAccountRepository accountRepo, CancellationToken ct)
    {
        if (batch.AccountId is null)
            throw new ImportBatchWithoutAccountException();

        return await accountRepo.GetByIdAsync(batch.AccountId.Value, ct)
            ?? throw new ImportAccountRequiredException();
    }

    private sealed record ConfirmItem(
        PreviewRowDto PreviewRow,
        ParsedImportRow Row,
        Account Account,
        bool Forced,
        bool AlreadyRecorded);

    private static List<string> RowErrors(IReadOnlyList<PreviewRowDto> rows)
        => rows
            .Where(r => r.Error is not null)
            .Select(r => $"Linha {r.RowIndex + 1}: {r.Error}")
            .ToList();
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

/// <summary>
/// JSON gravado no lote. <see cref="Settings"/> (Phase 6.5 grupo 7, Q3) é
/// <c>null</c> em lotes antigos.
/// </summary>
public sealed record PreviewWrapper(
    IReadOnlyList<string> Headers,
    IReadOnlyList<PreviewRowDto> Rows,
    ImportParseSettings? Settings = null);

public sealed record ImportConfirmResponse(
    Guid BatchId,
    int ImportedRows,
    int AutoCategorized,
    int ManualCount,
    int ErrorRows,
    string Status,
    int SkippedBeforeOpeningBalance = 0,
    int TransfersCreated = 0,
    int TransfersLinked = 0,
    int TransfersAlreadyRecorded = 0);
