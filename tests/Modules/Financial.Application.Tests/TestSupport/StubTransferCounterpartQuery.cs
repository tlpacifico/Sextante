using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Application.Tests.TestSupport;

/// <summary>
/// Devolve as candidatas registadas com <see cref="With"/> para
/// (conta, tipo), filtradas pela janela pedida; vazio por defeito.
/// </summary>
public sealed class StubTransferCounterpartQuery : ITransferCounterpartQuery
{
    private readonly Dictionary<(Guid AccountId, TransactionKind Kind), List<TransferCandidate>> _candidates = new();

    public StubTransferCounterpartQuery With(Guid accountId, TransactionKind kind, params TransferCandidate[] candidates)
    {
        _candidates[(accountId, kind)] = candidates.ToList();
        return this;
    }

    public Task<IReadOnlyList<TransferCandidate>> FindAsync(
        Guid accountId, TransactionKind kind, TransactionDirection direction, decimal amount, string currency,
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var found = _candidates.TryGetValue((accountId, kind), out var list)
            ? list.Where(c => c.Date >= from && c.Date <= to).ToList()
            : new List<TransferCandidate>();
        return Task.FromResult<IReadOnlyList<TransferCandidate>>(found);
    }
}
