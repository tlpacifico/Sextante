using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Application.Features.CsvImport;

/// <summary>
/// Phase 6.5 grupo 7 (achado I4 da revisão) — transações que o lote em curso
/// já planeou mas que ainda não estão na DB. Um CSV com a coluna Conta pode
/// trazer os dois lados da mesma transferência; sem isto, cada lado criaria
/// a sua contraperna (4 pernas em vez de 2).
/// </summary>
public sealed class ImportPendingLegs
{
    private readonly List<Entry> _entries = new();

    public void AddTransferLeg(
        Guid id, Guid accountId, TransactionDirection direction, decimal amount, string currency, DateOnly date,
        Guid counterpartAccountId)
        => _entries.Add(new Entry(id, accountId, TransactionKind.Transfer, direction, amount, currency, date, counterpartAccountId));

    public void AddRegular(
        Guid id, Guid accountId, TransactionDirection direction, decimal amount, string currency, DateOnly date)
        => _entries.Add(new Entry(id, accountId, TransactionKind.Regular, direction, amount, currency, date, null));

    /// <summary>Uma regular do lote passou a perna (LinkExisting).</summary>
    public void MarkAsTransferLeg(Guid id, Guid counterpartAccountId)
    {
        var index = _entries.FindIndex(e => e.Id == id);
        if (index >= 0)
        {
            _entries[index] = _entries[index] with
            {
                Kind = TransactionKind.Transfer,
                CounterpartAccountId = counterpartAccountId,
            };
        }
    }

    public IEnumerable<TransferCandidate> Find(
        Guid accountId, TransactionKind kind, TransactionDirection direction, decimal amount, string currency,
        DateOnly from, DateOnly to)
        => _entries
            .Where(e => e.AccountId == accountId
                && e.Kind == kind
                && e.Direction == direction
                && e.Amount == amount
                && e.Currency == currency
                && e.Date >= from
                && e.Date <= to)
            .Select(e => new TransferCandidate(e.Id, e.Date, e.CounterpartAccountId));

    private sealed record Entry(
        Guid Id,
        Guid AccountId,
        TransactionKind Kind,
        TransactionDirection Direction,
        decimal Amount,
        string Currency,
        DateOnly Date,
        Guid? CounterpartAccountId);
}
