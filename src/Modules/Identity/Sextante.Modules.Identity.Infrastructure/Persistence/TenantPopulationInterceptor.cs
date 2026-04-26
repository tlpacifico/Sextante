using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Popula <c>TenantId</c> automaticamente em entidades <see cref="ITenantOwned"/>
/// que entram em estado <see cref="EntityState.Added"/> sem tenant explícito
/// (tech-stack §4.1). Funciona em conjunto com o
/// <c>TenantConnectionInterceptor</c>, que aplica <c>SET app.current_tenant_id</c>
/// no Postgres para a verificação RLS.
/// </summary>
public sealed class TenantPopulationInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenantContext;

    public TenantPopulationInterceptor(ITenantContext tenantContext)
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

            // Permitir override explícito durante signup (cria-se uma
            // membership com TenantId conhecido antes do contexto resolver).
            if (owned.TenantId.Value != Guid.Empty)
            {
                continue;
            }

            entry.Property(nameof(ITenantOwned.TenantId)).CurrentValue = _tenantContext.TenantId;
        }
    }
}
