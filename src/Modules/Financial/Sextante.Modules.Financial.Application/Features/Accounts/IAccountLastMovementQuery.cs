namespace Sextante.Modules.Financial.Application.Features.Accounts;

/// <summary>
/// Data do último movimento de uma conta (qualquer tipo; o filtro global do EF
/// trata tenant e soft delete). Serve o corte de overlap dos extratos (Phase 6.6, D7).
/// </summary>
public interface IAccountLastMovementQuery
{
    /// <summary><c>null</c> quando a conta não tem movimentos.</summary>
    Task<DateOnly?> GetLastMovementDateAsync(Guid accountId, CancellationToken cancellationToken);
}
