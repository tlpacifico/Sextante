namespace Sextante.Modules.Financial.Domain.Transactions;

/// <summary>
/// <c>Regular</c> conta para receitas/despesas; <c>Transfer</c> é uma perna
/// de uma transferência entre contas do tenant; <c>Adjustment</c> é um
/// acerto de saldo. Só <c>Regular</c> entra nos totais (ADR-014).
/// </summary>
public enum TransactionKind
{
    Regular = 0,
    Transfer = 1,
    Adjustment = 2,
}
