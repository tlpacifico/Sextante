using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Infrastructure.Persistence;

/// <summary>
/// Espelho do <c>AuditingInterceptor</c> do Identity, isolado para o
/// scope do <c>FinancialDbContext</c>. Popula <c>CreatedAt</c>,
/// <c>UpdatedAt</c>, <c>Version</c> em entidades <see cref="IAuditable"/>.
/// </summary>
public sealed class FinancialAuditingInterceptor : SaveChangesInterceptor
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
            if (entry.Entity is not IAuditable auditable)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    auditable.CreatedAt = now;
                    auditable.UpdatedAt = now;
                    auditable.Version = 1;
                    break;

                case EntityState.Modified:
                    auditable.UpdatedAt = now;
                    auditable.Version += 1;
                    break;
            }
        }
    }
}
