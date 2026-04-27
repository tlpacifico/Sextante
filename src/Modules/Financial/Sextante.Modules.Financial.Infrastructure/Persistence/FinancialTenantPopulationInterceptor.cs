using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Infrastructure.Persistence;

/// <summary>
/// Popula <c>TenantId</c> automaticamente em entidades <see cref="ITenantOwned"/>
/// que entram em estado <see cref="EntityState.Added"/> sem tenant explícito
/// (tech-stack §4.1). Idêntico ao do Identity, escopo aplicado apenas ao
/// <c>FinancialDbContext</c>.
/// </summary>
public sealed class FinancialTenantPopulationInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;

    public FinancialTenantPopulationInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            PopulateTenant(eventData.Context.ChangeTracker.Entries());
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            PopulateTenant(eventData.Context.ChangeTracker.Entries());
        }

        return base.SavingChanges(eventData, result);
    }

    private void PopulateTenant(IEnumerable<EntityEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (entry.Entity is not ITenantOwned owned)
            {
                continue;
            }

            if (owned.TenantId.Value != Guid.Empty)
            {
                continue;
            }

            entry.Property(nameof(ITenantOwned.TenantId)).CurrentValue = _tenantContext.TenantId;
        }
    }
}
