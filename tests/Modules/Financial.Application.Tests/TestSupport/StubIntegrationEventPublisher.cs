using Sextante.Messaging;
using Sextante.Modules.Financial.Application.Common;

namespace Sextante.Modules.Financial.Application.Tests.TestSupport;

/// <summary>
/// Stub partilhado: regista cada evento publicado para asserts em tests
/// que verificam o "side effect" do publish (ex.: criar Budget alert).
/// </summary>
public sealed class StubIntegrationEventPublisher : IIntegrationEventPublisher
{
    public List<IIntegrationEvent> Published { get; } = new();

    public Task PublishAsync(IIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        Published.Add(@event);
        return Task.CompletedTask;
    }
}
