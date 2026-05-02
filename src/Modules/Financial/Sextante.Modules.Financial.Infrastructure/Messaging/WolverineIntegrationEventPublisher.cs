using Sextante.Messaging;
using Sextante.Modules.Financial.Application.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Infrastructure.Messaging;

/// <summary>
/// Implementação default de <see cref="IIntegrationEventPublisher"/>
/// para produção. Delega a <see cref="IMessageBus.PublishAsync"/> do
/// Wolverine, que respeita a outbox transacional configurada no host.
/// </summary>
public sealed class WolverineIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IMessageBus _bus;

    public WolverineIntegrationEventPublisher(IMessageBus bus)
    {
        _bus = bus;
    }

    public async Task PublishAsync(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        await _bus.PublishAsync(@event);
    }
}
