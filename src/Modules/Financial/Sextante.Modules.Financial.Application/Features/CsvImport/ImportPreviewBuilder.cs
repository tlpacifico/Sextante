using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.CsvImport;

/// <summary>
/// Phase 6.5 grupo 7 — um só cálculo do preview para o upload e para o
/// UpdatePreview: parse, conta da linha, "antes do saldo inicial",
/// duplicados por conta e regras (a categoria só se sugere se o tipo
/// casar com a direção do sinal, R1).
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
        var byId = categories.ToDictionary(c => c.Id);
        for (var k = 0; k < ruleCandidates.Count && k < results.Count; k++)
        {
            var result = results[k];
            var rowIndex = ruleCandidates[k].RowIndex;
            if (result.NewCategoryId is not { } categoryId
                || !byId.TryGetValue(categoryId, out var category)
                || Transaction.DirectionFor(category.Kind) != parsed[rowIndex]!.Value.Row.Direction)
            {
                continue;
            }

            rows[rowIndex] = rows[rowIndex] with
            {
                SuggestedCategoryId = category.Id,
                SuggestedCategoryName = category.Name,
                IsAutoCategorized = true,
            };
        }

        return rows;
    }
}
