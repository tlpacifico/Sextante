using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Features.Accounts;

/// <summary>
/// Calcula o saldo de contas on-demand: <c>OpeningBalance + Σ SignedAmount</c>
/// sobre todas as transações (Regular, Transfer, Adjustment) desde
/// <c>OpeningBalanceDate</c> — diferente dos totais de receita/despesa, que só
/// contam <c>Regular</c>. Implementação vive em Infrastructure (precisa de
/// acesso direto ao <c>FinancialDbContext</c>).
/// </summary>
public interface IAccountBalanceQuery
{
    /// <summary>
    /// Saldo "hoje" de todas as contas do tenant atual — uma chamada, sem N+1.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Money>> GetCurrentBalancesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saldo de uma conta; <c>null</c> se a conta não existir ou não pertencer
    /// ao tenant atual (o filtro global do EF já trata multi-tenancy, sem
    /// checagem manual). <paramref name="at"/> <c>null</c>: sem corte
    /// superior. <paramref name="at"/> = D: soma até ao fim do dia D (inclusive).
    /// </summary>
    Task<Money?> GetBalanceAsync(Guid accountId, DateOnly? at, CancellationToken cancellationToken);
}
