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
/// candidata, e <c>pending</c> junta às candidatas da DB as que o próprio
/// lote já planeou.
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
        ImportPendingLegs pending,
        HashSet<Guid> claimed,
        CancellationToken ct)
    {
        if (target is null || target.Id == account.Id)
        {
            return new ImportTransferResolution(ImportTransferStatus.InvalidTarget, null);
        }

        // Q1 — o outro extrato (ou uma linha anterior deste lote) já criou
        // esta perna nesta conta.
        var recorded = await FindRecordedLegAsync(
            account, direction, amount, currency, date, query, pending, claimed, ct, target.Id);
        if (recorded is not null)
        {
            return new ImportTransferResolution(ImportTransferStatus.AlreadyRecorded, recorded.TransactionId);
        }

        // Sem câmbio implícito para amarrar os dois valores: fica regular.
        if (!string.Equals(target.Currency, currency, StringComparison.Ordinal))
        {
            return new ImportTransferResolution(ImportTransferStatus.CurrencyMismatch, null);
        }

        var (from, to) = Window(date);
        var opposite = direction == TransactionDirection.Outflow
            ? TransactionDirection.Inflow
            : TransactionDirection.Outflow;
        var candidates = (await query.FindAsync(
                target.Id, TransactionKind.Regular, opposite, amount, currency, from, to, ct))
            .Concat(pending.Find(target.Id, TransactionKind.Regular, opposite, amount, currency, from, to));
        var counterpart = TransferCounterpartMatcher.Pick(candidates, date, claimed);
        if (counterpart is not null)
        {
            claimed.Add(counterpart.TransactionId);
            return new ImportTransferResolution(ImportTransferStatus.LinkExisting, counterpart.TransactionId);
        }

        return new ImportTransferResolution(ImportTransferStatus.CreateCounterpart, null);
    }

    /// <summary>
    /// Perna Transfer já existente (na DB ou planeada no lote) na conta, com a
    /// direção, o valor e a moeda da linha, a ±7 dias e não reclamada. Sem
    /// <paramref name="counterpartAccountId"/>, aceita qualquer contraparte —
    /// é o caso de uma linha sem regra de transferência (achado I3: só um
    /// dos extratos tem regra). Reclama a perna encontrada.
    /// </summary>
    public static async Task<TransferCandidate?> FindRecordedLegAsync(
        Account account,
        TransactionDirection direction,
        decimal amount,
        string currency,
        DateOnly date,
        ITransferCounterpartQuery query,
        ImportPendingLegs pending,
        HashSet<Guid> claimed,
        CancellationToken ct,
        Guid? counterpartAccountId = null)
    {
        var (from, to) = Window(date);
        var legs = (await query.FindAsync(
                account.Id, TransactionKind.Transfer, direction, amount, currency, from, to, ct))
            .Concat(pending.Find(account.Id, TransactionKind.Transfer, direction, amount, currency, from, to))
            .Where(l => counterpartAccountId is null || l.CounterpartAccountId == counterpartAccountId);
        var recorded = TransferCounterpartMatcher.Pick(legs, date, claimed);
        if (recorded is not null)
        {
            claimed.Add(recorded.TransactionId);
        }

        return recorded;
    }

    private static (DateOnly From, DateOnly To) Window(DateOnly date)
        => (date.AddDays(-TransferCounterpartMatcher.WindowDays), date.AddDays(TransferCounterpartMatcher.WindowDays));
}
