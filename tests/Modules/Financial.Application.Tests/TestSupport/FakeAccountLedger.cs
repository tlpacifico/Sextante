using Sextante.Modules.Financial.Application.Features.Accounts;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.TestSupport;

/// <summary>
/// Conta falsa para o corte de overlap: saldo inicial + movimentos com data; implementa as
/// duas consultas que o <c>StatementOverlapTrimmer</c> usa.
/// </summary>
public sealed class FakeAccountLedger(decimal opening, params (DateOnly Date, decimal Amount)[] movements)
    : IAccountBalanceQuery, IAccountLastMovementQuery
{
    public Task<IReadOnlyDictionary<Guid, Money>> GetCurrentBalancesAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task<Money?> GetBalanceAsync(Guid accountId, DateOnly? at, CancellationToken ct)
        => Task.FromResult<Money?>(new Money(
            opening + movements.Where(m => at is null || m.Date <= at).Sum(m => m.Amount), "EUR"));

    public Task<DateOnly?> GetLastMovementDateAsync(Guid accountId, CancellationToken ct)
        => Task.FromResult(movements.Length == 0 ? (DateOnly?)null : movements.Max(m => m.Date));
}
