using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sextante.Infrastructure.Jobs;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Financial.Application.Tests.RecurringRules;

public sealed class TenantAwareJobTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Payload = "test-payload";

    [Fact]
    public async Task RunAsync_throws_when_tenantId_is_empty()
    {
        var stubSetter = new StubTenantContextSetter();
        var stubHandler = new StubHandler();
        var provider = new StubServiceProvider()
            .With<ITenantContextSetter>(stubSetter)
            .With<ITenantAwareJobHandler<string>>(stubHandler);
        var scopeFactory = new StubScopeFactory(provider);
        var logger = NullLogger<TenantAwareJob<string>>.Instance;
        var job = new TenantAwareJob<string>(scopeFactory, logger);

        var act = () => job.RunAsync(Guid.Empty, Payload, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunAsync_sets_tenant_and_calls_handler()
    {
        var stubSetter = new StubTenantContextSetter();
        var stubHandler = new StubHandler();
        var provider = new StubServiceProvider()
            .With<ITenantContextSetter>(stubSetter)
            .With<ITenantAwareJobHandler<string>>(stubHandler);
        var scopeFactory = new StubScopeFactory(provider);
        var logger = NullLogger<TenantAwareJob<string>>.Instance;
        var job = new TenantAwareJob<string>(scopeFactory, logger);

        await job.RunAsync(TenantId, Payload, CancellationToken.None);

        stubSetter.LastSetTenantId.Should().Be(TenantId);
        stubSetter.ClearCallCount.Should().Be(1);
        stubHandler.LastTenantId.Should().Be(TenantId);
        stubHandler.LastPayload.Should().Be(Payload);
    }

    [Fact]
    public async Task RunAsync_calls_Clear_even_when_handler_throws()
    {
        var stubSetter = new StubTenantContextSetter();
        var stubHandler = new StubHandler { ShouldThrow = true };
        var provider = new StubServiceProvider()
            .With<ITenantContextSetter>(stubSetter)
            .With<ITenantAwareJobHandler<string>>(stubHandler);
        var scopeFactory = new StubScopeFactory(provider);
        var logger = NullLogger<TenantAwareJob<string>>.Instance;
        var job = new TenantAwareJob<string>(scopeFactory, logger);

        var act = () => job.RunAsync(TenantId, Payload, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        stubSetter.ClearCallCount.Should().Be(1);
    }

    // ── Stubs ────────────────────────────────────────────────────────

    private sealed class StubScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;

        public StubScopeFactory(IServiceProvider provider) => _provider = provider;

        public IServiceScope CreateScope() => new StubScope(_provider);

        public AsyncServiceScope CreateAsyncScope() => new(new StubScope(_provider));
    }

    private sealed class StubScope : IServiceScope
    {
        public StubScope(IServiceProvider provider) => ServiceProvider = provider;

        public IServiceProvider ServiceProvider { get; }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubTenantContextSetter : ITenantContextSetter
    {
        public Guid? LastSetTenantId { get; private set; }
        public int ClearCallCount { get; private set; }

        public void SetCurrent(Guid tenantId) => LastSetTenantId = tenantId;

        public void Clear() => ClearCallCount++;
    }

    private sealed class StubServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = new();

        public StubServiceProvider With<T>(T instance) where T : class
        {
            _services[typeof(T)] = instance;
            return this;
        }

        public object? GetService(Type serviceType) =>
            _services.TryGetValue(serviceType, out var svc) ? svc : null;
    }

    private sealed class StubHandler : ITenantAwareJobHandler<string>
    {
        public Guid? LastTenantId { get; private set; }
        public string? LastPayload { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task ExecuteAsync(Guid tenantId, string payload, CancellationToken ct)
        {
            LastTenantId = tenantId;
            LastPayload = payload;
            if (ShouldThrow)
                throw new InvalidOperationException("forced error");
            return Task.CompletedTask;
        }
    }
}
