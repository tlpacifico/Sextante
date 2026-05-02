using Sextante.Messaging;

namespace Sextante.Modules.Financial.PublicApi.Events;

/// <summary>
/// Publicado quando uma Transaction é atualizada (Phase 2 update flow).
/// Subscribers in-process: Phase 5b <c>BudgetAlertDispatchHandler</c>
/// recalcula budgets afetados. <c>OccurredAtTransaction</c> reflete o
/// valor atualizado (pode ter mudado a categoria ou a data).
///
/// Sem PII: apenas IDs, valores monetários e datas.
/// </summary>
public sealed record TransactionUpdatedIntegrationEvent(
    Guid TransactionId,
    Guid TenantId,
    Guid AccountId,
    Guid? CategoryId,
    decimal AmountAmount,
    string AmountCurrency,
    DateTimeOffset OccurredAtTransaction,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
