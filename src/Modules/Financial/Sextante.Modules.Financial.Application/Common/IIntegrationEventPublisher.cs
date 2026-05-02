using Sextante.Messaging;

namespace Sextante.Modules.Financial.Application.Common;

/// <summary>
/// Thin abstraction sobre <c>Wolverine.IMessageBus.PublishAsync</c>.
/// Mantém handlers de Application livres da dependência directa em
/// Wolverine, simplifica unit tests (stub trivial em vez de mock de
/// uma interface enorme) e permite trocar o transporte sem tocar nos
/// handlers. Implementação real em Infrastructure.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(IIntegrationEvent @event, CancellationToken cancellationToken = default);
}
