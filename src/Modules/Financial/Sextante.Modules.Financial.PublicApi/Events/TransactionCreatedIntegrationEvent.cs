using Sextante.Messaging;

namespace Sextante.Modules.Financial.PublicApi.Events;

/// <summary>
/// Publicado quando uma Transaction é criada (via comando manual ou via
/// materialização de regra recorrente). Subscribers in-process: Phase 5b
/// <c>BudgetAlertDispatchHandler</c> recalcula budgets afetados.
/// Phase 15 (notificações) pode subscrever para push/email.
///
/// Sem PII: apenas IDs, valores monetários e datas. Descrição e tags
/// não são propagadas para evitar leak para subscribers que não têm
/// necessidade legítima desses campos.
/// </summary>
public sealed record TransactionCreatedIntegrationEvent(
    Guid TransactionId,
    Guid TenantId,
    Guid AccountId,
    Guid? CategoryId,
    decimal AmountAmount,
    string AmountCurrency,
    DateTimeOffset OccurredAtTransaction,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
