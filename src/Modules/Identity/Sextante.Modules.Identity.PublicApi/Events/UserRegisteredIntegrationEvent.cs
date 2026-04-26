using Sextante.Messaging;

namespace Sextante.Modules.Identity.PublicApi.Events;

/// <summary>
/// Publicado quando um signup completa com sucesso (User + Tenant +
/// Membership criados atomicamente). Phase 2 (módulo Financial) liga
/// um subscriber para semear categorias default.
/// </summary>
public sealed record UserRegisteredIntegrationEvent(
    Guid UserId,
    Guid TenantId,
    string Email,
    DateTimeOffset OccurredAt) : IIntegrationEvent;
