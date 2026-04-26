namespace Sextante.Messaging;

/// <summary>
/// Marker para eventos de integração publicados em <c>*.PublicApi</c> e
/// transportados pelo bus inter-módulos (Wolverine + Postgres outbox).
/// </summary>
public interface IIntegrationEvent
{
    DateTimeOffset OccurredAt { get; }
}
