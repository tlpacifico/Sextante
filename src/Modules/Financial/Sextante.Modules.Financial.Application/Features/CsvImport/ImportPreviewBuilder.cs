using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.CsvImport;

/// <summary>
/// Phase 6.5 grupo 7 — um só cálculo do preview para o upload e para o
/// UpdatePreview: parse, conta da linha, "antes do saldo inicial",
/// duplicados por conta e regras: a categoria só se sugere se o tipo casar
/// com a direção do sinal (R1); regras de transferência resolvem a
/// contraperna (R2).
/// </summary>
public static class ImportPreviewBuilder
{
    public static async Task<IReadOnlyList<PreviewRowDto>> BuildAsync(
        IReadOnlyList<IReadOnlyList<string>> rawRows,
        CsvColumnResolver resolver,
        ImportParseSettings settings,
        Account batchAccount,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<Category> categories,
        IDuplicateDetector duplicateDetector,
        ICategorizationRuleEngine ruleEngine,
        ITransferCounterpartQuery transferQuery,
        TenantId tenantId,
        CancellationToken ct)
    {
        var rows = new PreviewRowDto[rawRows.Count];
        var parsed = new (ParsedImportRow Row, Account Account)?[rawRows.Count];

        for (var i = 0; i < rawRows.Count; i++)
        {
            var values = rawRows[i];
            var row = ImportRowParser.Parse(values, resolver, settings, out var error);
            Account? account = null;
            if (row is not null)
            {
                account = ImportRowParser.ResolveAccount(row, batchAccount, accounts, out error);
            }

            if (row is null || account is null)
            {
                rows[i] = new PreviewRowDto(i, values, false, null, null, null, false, error);
                continue;
            }

            parsed[i] = (row, account);
            rows[i] = new PreviewRowDto(
                i, values, false, null, null, null, false, null,
                AccountId: account.Id,
                IsBeforeOpeningBalance: row.Date < account.OpeningBalanceDate);
        }

        // Duplicados (R6): mesma conta, data, valor absoluto, moeda e descrição.
        var toCheck = new List<ParsedTransaction>();
        for (var i = 0; i < parsed.Length; i++)
        {
            if (parsed[i] is not { } p) continue;
            toCheck.Add(new ParsedTransaction(
                i,
                p.Row.Date,
                p.Row.AbsAmount,
                p.Row.Currency ?? p.Account.Currency,
                p.Row.Description ?? string.Empty,
                p.Account.Id));
        }

        foreach (var match in await duplicateDetector.FindPotentialDuplicatesAsync(toCheck, tenantId, ct))
        {
            rows[match.RowIndex] = rows[match.RowIndex] with
            {
                IsDuplicate = true,
                DuplicateTransactionId = match.ExistingTransactionId,
            };
        }

        // Regras de categorização, só nas linhas com descrição.
        var ruleCandidates = new List<(int RowIndex, TransactionToCategorize Tx)>();
        for (var i = 0; i < parsed.Length; i++)
        {
            if (parsed[i] is not { } p || string.IsNullOrWhiteSpace(p.Row.Description)) continue;
            ruleCandidates.Add((i, new TransactionToCategorize(Guid.CreateVersion7(), p.Row.Description)));
        }

        var results = await ruleEngine.ApplyAsync(ruleCandidates.Select(c => c.Tx).ToList(), tenantId, ct);
        var resultByRow = new Dictionary<int, CategorizationMatchResult>();
        for (var k = 0; k < ruleCandidates.Count && k < results.Count; k++)
        {
            resultByRow[ruleCandidates[k].RowIndex] = results[k];
        }

        var byId = categories.ToDictionary(c => c.Id);
        var accountsById = accounts.ToDictionary(a => a.Id);
        var claimed = new HashSet<Guid>();
        var pending = new ImportPendingLegs();

        // Por ordem do ficheiro, como o confirm: cada linha vê o que as
        // anteriores do lote já planearam (achado I4).
        for (var i = 0; i < parsed.Length; i++)
        {
            // R6 — duplicado "normal" não passa pela resolução.
            if (parsed[i] is not { } p || rows[i].IsDuplicate) continue;

            var (row, account) = p;
            var currency = row.Currency ?? account.Currency;
            resultByRow.TryGetValue(i, out var result);

            if (result?.TargetAccountId is { } targetId)
            {
                accountsById.TryGetValue(targetId, out var target);
                var resolution = await ImportTransferResolver.ResolveAsync(
                    account, target, row.Direction, row.AbsAmount, currency, row.Date, transferQuery, pending, claimed, ct);

                rows[i] = rows[i] with
                {
                    TransferStatus = resolution.Status.ToString(),
                    TransferTargetAccountId = targetId,
                    TransferTargetAccountName = target?.Name,
                    TransferCounterpartTransactionId = resolution.TransactionId,
                };
                switch (resolution.Status)
                {
                    case ImportTransferStatus.AlreadyRecorded:
                        rows[i] = rows[i] with { IsDuplicate = true, DuplicateTransactionId = resolution.TransactionId };
                        break;
                    case ImportTransferStatus.CreateCounterpart:
                        PlanTransfer(pending, row, account, currency, target!, createCounterpart: true);
                        break;
                    case ImportTransferStatus.LinkExisting:
                        PlanTransfer(pending, row, account, currency, target!, createCounterpart: false);
                        pending.MarkAsTransferLeg(resolution.TransactionId!.Value, account.Id);
                        break;
                    default:
                        pending.AddRegular(Guid.NewGuid(), account.Id, row.Direction, row.AbsAmount, currency, row.Date);
                        break;
                }

                continue;
            }

            // Achado I3 — sem regra de transferência, mas o outro extrato já
            // criou esta perna: "já registada", como com regra.
            var recorded = await ImportTransferResolver.FindRecordedLegAsync(
                account, row.Direction, row.AbsAmount, currency, row.Date, transferQuery, pending, claimed, ct);
            if (recorded is not null)
            {
                Account? counterpartAccount = null;
                if (recorded.CounterpartAccountId is { } counterpartId)
                    accountsById.TryGetValue(counterpartId, out counterpartAccount);
                rows[i] = rows[i] with
                {
                    IsDuplicate = true,
                    DuplicateTransactionId = recorded.TransactionId,
                    TransferStatus = nameof(ImportTransferStatus.AlreadyRecorded),
                    TransferTargetAccountId = recorded.CounterpartAccountId,
                    TransferTargetAccountName = counterpartAccount?.Name,
                    TransferCounterpartTransactionId = recorded.TransactionId,
                };
                continue;
            }

            pending.AddRegular(Guid.NewGuid(), account.Id, row.Direction, row.AbsAmount, currency, row.Date);

            if (result?.NewCategoryId is not { } categoryId
                || !byId.TryGetValue(categoryId, out var category)
                || Transaction.DirectionFor(category.Kind) != row.Direction)
            {
                continue;
            }

            rows[i] = rows[i] with
            {
                SuggestedCategoryId = category.Id,
                SuggestedCategoryName = category.Name,
                IsAutoCategorized = true,
            };
        }

        return rows;
    }

    /// <summary>Regista no lote as pernas que o confirm vai criar para esta linha.</summary>
    private static void PlanTransfer(
        ImportPendingLegs pending, ParsedImportRow row, Account account, string currency, Account target,
        bool createCounterpart)
    {
        pending.AddTransferLeg(Guid.NewGuid(), account.Id, row.Direction, row.AbsAmount, currency, row.Date, target.Id);
        if (createCounterpart)
        {
            var opposite = row.Direction == TransactionDirection.Outflow
                ? TransactionDirection.Inflow
                : TransactionDirection.Outflow;
            pending.AddTransferLeg(Guid.NewGuid(), target.Id, opposite, row.AbsAmount, currency, row.Date, account.Id);
        }
    }
}
