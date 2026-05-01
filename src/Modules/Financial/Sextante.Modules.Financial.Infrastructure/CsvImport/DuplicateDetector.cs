using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Infrastructure.Persistence;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Infrastructure.CsvImport;

public sealed class DuplicateDetector : IDuplicateDetector
{
    private readonly FinancialDbContext _db;

    public DuplicateDetector(FinancialDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DuplicateMatch>> FindPotentialDuplicatesAsync(
        IReadOnlyList<ParsedTransaction> previewRows,
        TenantId tenantId,
        CancellationToken ct)
    {
        var matches = new List<DuplicateMatch>();

        var batches = previewRows
            .Select((row, index) => (row, index))
            .GroupBy(x => x.index / 100)
            .Select(g => g.Select(x => x.row).ToList());

        foreach (var batch in batches)
        {
            var dates = batch.Select(r => r.Date).Distinct().ToList();
            var amounts = batch.Select(r => r.Amount).Distinct().ToList();
            var currencies = batch.Select(r => r.Currency).Distinct().ToList();

            var candidates = await _db.Transactions
                .Where(t => t.TenantId == tenantId
                         && dates.Contains(DateOnly.FromDateTime(t.OccurredAt.UtcDateTime))
                         && amounts.Contains(t.Amount.Amount)
                         && currencies.Contains(t.Amount.Currency))
                .Select(t => new
                {
                    t.Id,
                    OccurredAt = t.OccurredAt,
                    Amount = t.Amount.Amount,
                    Currency = t.Amount.Currency,
                    t.Description,
                })
                .ToListAsync(ct);

            foreach (var row in batch)
            {
                var rowDescription = NormalizeDescription(row.Description);
                var match = candidates.FirstOrDefault(c =>
                    DateOnly.FromDateTime(c.OccurredAt.UtcDateTime) == row.Date
                    && c.Amount == row.Amount
                    && c.Currency == row.Currency
                    && (c.Description is not null
                        && NormalizeDescription(c.Description) == rowDescription));
                if (match is not null)
                    matches.Add(new DuplicateMatch(row.RowIndex, match.Id));
            }
        }

        return matches;
    }

    public static string NormalizeDescription(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var normalized = input.ToLowerInvariant().Trim();
        normalized = RemoveDiacritics(normalized);
        normalized = Regex.Replace(normalized, @"[.,;:\-_]", " ", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized;
    }

    private static string RemoveDiacritics(string text)
    {
        var chars = text
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC);
    }
}
