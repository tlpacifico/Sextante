namespace Sextante.Modules.Financial.Domain.Transactions;

/// <summary>
/// Sentido do movimento na conta. <c>Amount</c> é sempre positivo; o sinal
/// vive aqui (Phase 6.5, ADR-014).
/// </summary>
public enum TransactionDirection
{
    Inflow = 0,
    Outflow = 1,
}
