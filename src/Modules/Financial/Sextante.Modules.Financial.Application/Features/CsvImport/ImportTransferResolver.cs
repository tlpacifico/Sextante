using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Application.Features.CsvImport;

public enum ImportTransferStatus
{
    CreateCounterpart,
    LinkExisting,
    AlreadyRecorded,
    CurrencyMismatch,
    InvalidTarget,
}

/// <summary>
/// <see cref="TransactionId"/> = contraperna a ligar (LinkExisting) ou perna
/// já registada (AlreadyRecorded); <c>null</c> nos restantes.
/// </summary>
public sealed record ImportTransferResolution(ImportTransferStatus Status, Guid? TransactionId);

/// <summary>
/// Phase 6.5 grupo 7 (R2, D9) — o que fazer com uma linha importada que
/// uma regra MarkAsTransfer marcou. Mesma função no preview e no confirm;
/// <c>claimed</c> impede duas linhas do mesmo lote de ficarem com a mesma
/// candidata.
/// </summary>
public static class ImportTransferResolver
{
    public static async Task<ImportTransferResolution> ResolveAsync(
        Account account,
        Account? target,
        TransactionDirection direction,
        decimal amount,
        string currency,
        DateOnly date,
        ITransferCounterpartQuery query,
        HashSet<Guid> claimed,
        CancellationToken ct)
    {
        if (target is null || target.Id == account.Id)
        {
            return new ImportTransferResolution(ImportTransferStatus.InvalidTarget, null);
        }

        var from = date.AddDays(-TransferCounterpartMatcher.WindowDays);
        var to = date.AddDays(TransferCounterpartMatcher.WindowDays);

        // Q1 — o outro extrato já criou esta perna nesta conta.
        var legs = await query.FindAsync(
            account.Id, TransactionKind.Transfer, direction, amount, currency, from, to, ct);
        var recorded = TransferCounterpartMatcher.Pick(
            legs.Where(l => l.CounterpartAccountId == target.Id), date, claimed);
        if (recorded is not null)
        {
            claimed.Add(recorded.TransactionId);
            return new ImportTransferResolution(ImportTransferStatus.AlreadyRecorded, recorded.TransactionId);
        }

        // Sem câmbio implícito para amarrar os dois valores: fica regular.
        if (!string.Equals(target.Currency, currency, StringComparison.Ordinal))
        {
            return new ImportTransferResolution(ImportTransferStatus.CurrencyMismatch, null);
        }

        var opposite = direction == TransactionDirection.Outflow
            ? TransactionDirection.Inflow
            : TransactionDirection.Outflow;
        var candidates = await query.FindAsync(
            target.Id, TransactionKind.Regular, opposite, amount, currency, from, to, ct);
        var counterpart = TransferCounterpartMatcher.Pick(candidates, date, claimed);
        if (counterpart is not null)
        {
            claimed.Add(counterpart.TransactionId);
            return new ImportTransferResolution(ImportTransferStatus.LinkExisting, counterpart.TransactionId);
        }

        return new ImportTransferResolution(ImportTransferStatus.CreateCounterpart, null);
    }
}
