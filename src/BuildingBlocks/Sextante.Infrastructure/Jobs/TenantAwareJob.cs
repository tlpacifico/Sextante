using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Infrastructure.Jobs;

public interface ITenantAwareJobHandler<in TPayLoad>
{
    Task ExecuteAsync(Guid tenantId, TPayLoad payload, CancellationToken ct);
}

public sealed class TenantAwareJob<TPayLoad>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantAwareJob<TPayLoad>> _logger;

    public TenantAwareJob(
        IServiceScopeFactory scopeFactory,
        ILogger<TenantAwareJob<TPayLoad>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task RunAsync(Guid tenantId, TPayLoad payload, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId não pode ser Guid.Empty em TenantAwareJob.", nameof(tenantId));
        }

        await using var scope = _scopeFactory.CreateAsyncScope();

        // Resolve o setter do scope filho e configura o tenant.
        var tenantSetter = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>();
        tenantSetter.SetCurrent(tenantId);

        try
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ITenantAwareJobHandler<TPayLoad>>();

            await handler.ExecuteAsync(tenantId, payload, ct);
        }
        finally
        {
            tenantSetter.Clear();
        }
    }
}
