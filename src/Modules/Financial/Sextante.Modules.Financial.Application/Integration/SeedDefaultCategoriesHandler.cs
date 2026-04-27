using Microsoft.Extensions.Logging;
using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Identity.PublicApi.Events;
using Sextante.SharedKernel;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Integration;

/// <summary>
/// Subscriber Wolverine para <see cref="UserRegisteredIntegrationEvent"/>.
/// Cria as 11 categorias seed para o tenant recém-criado. O TenantId vem
/// do evento (não do <c>ITenantContext</c>) para que o handler possa
/// correr fora do scope da request HTTP — e.g. retry pós-failure.
/// </summary>
[NonTransactional]
public static class SeedDefaultCategoriesHandler
{
    public static async Task Handle(
        UserRegisteredIntegrationEvent @event,
        ICategoryRepository repository,
        ILogger<UserRegisteredIntegrationEvent> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = new TenantId(@event.TenantId);
        var categories = DefaultCategories.All
            .Select(seed => Category.Create(
                seed.Name,
                seed.Kind,
                seed.IconName,
                seed.ColorHex,
                tenantId))
            .ToList();

        await repository.SeedAsync(@event.TenantId, categories, cancellationToken);

        logger.LogInformation(
            "Seeded {Count} categorias default para tenant {TenantId}.",
            categories.Count,
            @event.TenantId);
    }
}
