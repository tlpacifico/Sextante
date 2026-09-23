using Sextante.Modules.Financial.Domain.Accounts;

namespace Sextante.Modules.Financial.Application.Features.Accounts;

/// <summary>
/// Movimentos de uma conta para o extrato do cartão (Phase 6.5 grupo 5).
/// Implementação em Infrastructure (acesso direto ao <c>FinancialDbContext</c>,
/// como <see cref="IAccountBalanceQuery"/>).
/// </summary>
public interface ICreditCardActivityQuery
{
    /// <summary>
    /// Movimentos não apagados da conta com data UTC em
    /// [<paramref name="from"/>, <paramref name="to"/>], inclusivo.
    /// </summary>
    Task<IReadOnlyList<CreditCardMovement>> GetMovementsAsync(
        Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
