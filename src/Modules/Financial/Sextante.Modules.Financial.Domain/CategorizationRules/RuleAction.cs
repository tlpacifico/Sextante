namespace Sextante.Modules.Financial.Domain.CategorizationRules;

/// <summary>
/// O que uma regra faz a uma transação que casa com o padrão (Phase 6.5).
/// </summary>
public enum RuleAction
{
    SetCategory = 0,
    MarkAsTransfer = 1,
}
