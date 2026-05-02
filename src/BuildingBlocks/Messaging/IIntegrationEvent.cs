namespace Sextante.Messaging;

/// <summary>
/// Marker para eventos de integração publicados em <c>*.PublicApi</c> e
/// transportados pelo bus inter-módulos (Wolverine + Postgres outbox).
/// </summary>
public interface IIntegrationEvent
{
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// Eventos de integração que carregam <c>TenantId</c>. Permite que o
/// pipeline Wolverine configure <c>ITenantContextSetter</c> antes do
/// handler correr — o handler é processado num scope de background sem
/// <c>HttpContext</c>, portanto sem este marker o Global Query Filter
/// do EF Core falha-loud (tech-stack.md §4.4).
///
/// Cumpre o mesmo papel que <c>TenantAwareJob&lt;T&gt;</c> faz para
/// Hangfire, mas declarativo via tipo do evento.
/// </summary>
public interface ITenantOwnedIntegrationEvent : IIntegrationEvent
{
    Guid TenantId { get; }
}
