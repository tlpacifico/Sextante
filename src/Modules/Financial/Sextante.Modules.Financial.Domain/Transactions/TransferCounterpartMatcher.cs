namespace Sextante.Modules.Financial.Domain.Transactions;

/// <summary>
/// Candidata a contraperna (ou perna já registada) de uma transferência.
/// <see cref="CounterpartAccountId"/> só vem preenchido para pernas Transfer.
/// </summary>
public sealed record TransferCandidate(Guid TransactionId, DateOnly Date, Guid? CounterpartAccountId);

/// <summary>
/// Phase 6.5 D9 — escolhe a contraperna de uma transferência entre as
/// candidatas: débito na conta e crédito no cartão têm datas diferentes
/// (ex.: 11/09 vs 14/09), por isso aceita uma janela de dias.
/// </summary>
public static class TransferCounterpartMatcher
{
    public const int WindowDays = 7;

    /// <summary>
    /// |Δ| ≤ <see cref="WindowDays"/> e não reclamada; menor |Δdias|, depois a
    /// data mais antiga, depois o Id mais baixo — determinístico.
    /// </summary>
    public static TransferCandidate? Pick(
        IEnumerable<TransferCandidate> candidates, DateOnly date, IReadOnlySet<Guid> claimed)
        => candidates
            .Where(c => !claimed.Contains(c.TransactionId)
                && Math.Abs(c.Date.DayNumber - date.DayNumber) <= WindowDays)
            .OrderBy(c => Math.Abs(c.Date.DayNumber - date.DayNumber))
            .ThenBy(c => c.Date)
            .ThenBy(c => c.TransactionId)
            .FirstOrDefault();
}
