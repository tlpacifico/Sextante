using System.Text.Json;
using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.ImportBatches;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.CsvImport;

/// <summary>
/// Phase 6.6 — upload de extratos XLSX / PDF / JSON. Converte e valida (tudo-ou-nada, D3),
/// corta o overlap com a conta e entra no <b>mesmo</b> preview e <see cref="ImportBatch"/>
/// do fluxo CSV, por linhas canónicas (D2). Uma validação que falha lança antes de criar
/// o lote: nada fica gravado. Chamado diretamente pelo endpoint (como o upload CSV); o nome
/// não termina em "Handlers" para o Wolverine não o descobrir como handler de mensagens.
/// </summary>
public static class StatementUploadService
{
    public static async Task<UploadCsvResponse> UploadAsync(
        Stream fileStream,
        string fileName,
        Guid? accountId,
        IEnumerable<IStatementConverter> converters,
        StatementOverlapTrimmer overlapTrimmer,
        IImportBatchRepository batchRepo,
        IAccountRepository accountRepo,
        ICategoryRepository categoryRepo,
        ICategorizationRuleEngine ruleEngine,
        ITransferCounterpartQuery transferQuery,
        ITenantContext tenant,
        CancellationToken ct)
    {
        if (accountId is null)
            throw new ImportAccountRequiredException();
        var account = await accountRepo.GetByIdAsync(accountId.Value, ct)
            ?? throw new ImportAccountRequiredException();

        var converter = converters.FirstOrDefault(c => c.CanHandle(fileName))
            ?? throw new StatementUnreadableException("tipo de ficheiro não suportado (use .csv, .xlsx, .pdf ou .json).");

        StatementConversionResult converted;
        try
        {
            converted = converter.Convert(fileStream, ct);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or FinancialDomainException))
        {
            // Entrada não confiável: um ficheiro que parte um parser é 400, nunca 500.
            throw new StatementUnreadableException("o ficheiro tem um conteúdo inesperado.");
        }

        // D14 — sem coluna de moeda no formato canónico: converter em silêncio corromperia saldos.
        if (!string.Equals(converted.Currency, account.Currency, StringComparison.OrdinalIgnoreCase))
            throw new StatementCurrencyMismatchException(converted.Currency, account.Currency);

        var statement = await overlapTrimmer.TrimAsync(converted, account.Id, ct);
        var csv = CanonicalCsvAdapter.Adapt(statement);

        var headers = csv.Headers.ToList();
        var rawRows = csv.Rows.Select(r => (IReadOnlyList<string>)r.ToList()).ToList();
        var resolver = ImportRowParser.ResolverFor(headers, csv.Settings, profile: null);

        var rows = await ImportPreviewBuilder.BuildAsync(
            rawRows,
            resolver,
            csv.Settings,
            account,
            await accountRepo.ListAsync(ct),
            await categoryRepo.ListAsync(ct),
            NoDuplicateDetector.Instance,
            ruleEngine,
            transferQuery,
            tenant.TenantId,
            ct);

        var batch = ImportBatch.StartParsing(fileName, tenant.TenantId, importProfileId: null, account.Id);
        batch.SetPreview(
            rawRows.Count,
            JsonSerializer.Serialize(new PreviewWrapper(headers, rows, csv.Settings)),
            truncated: false,
            rows.Count(r => r.Error is not null));

        await batchRepo.AddAsync(batch, ct);
        await batchRepo.SaveChangesAsync(ct);

        return new UploadCsvResponse(
            batch.Id,
            headers,
            rows,
            rawRows.Count,
            Truncated: false,
            DetectedDelimiter: ";",
            DetectedHasHeader: true,
            Errors: CsvImportHandlers.RowErrors(rows),
            Statement: Summarize(statement));
    }

    /// <summary>
    /// O overlap já foi cortado pelo saldo (D7). O detetor heurístico (mesma data, valor e
    /// descrição) voltaria a marcar como duplicada a 2.ª de duas transferências idênticas do
    /// mesmo dia — exatamente o caso que o corte por saldo resolve —, por isso não se usa aqui.
    /// </summary>
    private sealed class NoDuplicateDetector : IDuplicateDetector
    {
        public static readonly NoDuplicateDetector Instance = new();

        public Task<IReadOnlyList<DuplicateMatch>> FindPotentialDuplicatesAsync(
            IReadOnlyList<ParsedTransaction> previewRows, TenantId tenantId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DuplicateMatch>>([]);
    }

    private static StatementSummary Summarize(StatementConversionResult result)
        => new(
            result.Format.ToString(),
            result.Currency,
            result.PeriodStart?.ToString("yyyy-MM-dd"),
            result.PeriodEnd?.ToString("yyyy-MM-dd"),
            result.BalanceBefore,
            result.BalanceAfter,
            result.Checks.Select(c => new StatementCheckDto(c.Name, c.Passed, c.Expected, c.Actual)).ToList(),
            result.RowsTrimmed,
            result.PendingIgnored,
            result.CancelledIgnored);
}
