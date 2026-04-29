using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Popula <see cref="IAuditable.CreatedAt"/> em entidades novas e
/// <see cref="IAuditable.UpdatedAt"/>+<c>Version</c> em modificadas.
/// Não toca em tabelas Identity built-in — essas têm o seu próprio
/// versionamento via <c>ConcurrencyStamp</c>.
/// </summary>
public sealed class AuditingInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            ApplyAudit(eventData.Context.ChangeTracker.Entries());
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            ApplyAudit(eventData.Context.ChangeTracker.Entries());
        }

        return base.SavingChanges(eventData, result);
    }

    private static void ApplyAudit(IEnumerable<EntityEntry> entries)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.Entity is not IVersioned versioned)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    versioned.CreatedAt = now;
                    versioned.UpdatedAt = now;
                    versioned.Version = 1;
                    break;

                case EntityState.Modified:
                    versioned.UpdatedAt = now;
                    versioned.Version += 1;
                    break;
            }
        }
    }
}
