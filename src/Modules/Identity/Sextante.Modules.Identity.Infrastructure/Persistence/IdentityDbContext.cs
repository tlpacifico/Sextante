using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Domain.Enums;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    private readonly ITenantContext? _tenantContext;

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("shared");

        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("Tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).IsRequired().HasMaxLength(200);
            b.Property(t => t.CreatedAt).IsRequired();
            b.Property(t => t.UpdatedAt).IsRequired();
            b.Property(t => t.Version).IsConcurrencyToken();
            b.HasIndex(t => t.Name);
        });

        modelBuilder.Entity<Membership>(b =>
        {
            b.ToTable("Memberships");
            b.HasKey(m => m.Id);
            b.Property(m => m.Role)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            b.Property(m => m.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .IsRequired();
            b.Property(m => m.UserId).IsRequired();
            b.Property(m => m.CreatedAt).IsRequired();
            b.Property(m => m.UpdatedAt).IsRequired();
            b.Property(m => m.Version).IsConcurrencyToken();

            b.HasIndex(m => new { m.UserId, m.TenantId }).IsUnique();
            b.HasIndex(m => m.TenantId);

            b.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Sem FK formal a Tenant porque o TenantId é value object e
            // EF Core não consegue compor a relação automaticamente. A FK
            // é criada via Sql() na migration EnableRowLevelSecurity (junto
            // com os ALTER TABLE da policy).

            // Filtro global: respeita o TenantContext se autenticado.
            // Quando _tenantContext é null (migrations runner ou tooling),
            // o filtro está inativo; em runtime DI fornece o contexto.
            b.HasQueryFilter(m =>
                _tenantContext == null || m.TenantId == _tenantContext.TenantId);
        });
    }
}
