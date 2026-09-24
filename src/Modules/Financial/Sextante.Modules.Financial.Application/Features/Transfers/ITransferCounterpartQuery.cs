using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Application.Features.Transfers;

/// <summary>
/// Phase 6.5 grupo 7 — candidatas a contraperna de uma transferência (D9),
/// para o import e para reaplicar regras. O filtro global do EF trata do
/// tenant e do soft delete.
/// </summary>
public interface ITransferCounterpartQuery
{
    /// <summary>
    /// Transações da conta com a direção, o valor e a moeda dados e data (UTC)
    /// em [<paramref name="from"/>, <paramref name="to"/>].
    /// <paramref name="kind"/> = Regular → só as que ainda não têm TransferId
    /// (CounterpartAccountId = null). Transfer → pernas, com
    /// CounterpartAccountId = conta da outra perna, e só as que nenhum extrato
    /// desta conta confirmou ainda (<see cref="Transaction.StatementConfirmed"/>).
    /// </summary>
    Task<IReadOnlyList<TransferCandidate>> FindAsync(
        Guid accountId,
        TransactionKind kind,
        TransactionDirection direction,
        decimal amount,
        string currency,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);
}
