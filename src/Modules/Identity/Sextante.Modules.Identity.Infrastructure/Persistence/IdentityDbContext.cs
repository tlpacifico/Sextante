using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext : IdentityDbContext<AppUser, AppRole, Guid>
{
    private readonly ITenantContext? _tenantContext;

    /// <summary>
    /// <paramref name="tenantContext"/> é nullable: em runtime DI fornece o
    /// scoped <see cref="TenantContext"/>; o <see cref="MigrationRunner"/>
    /// passa <c>null</c> para correr migrations sem dependência de
    /// <c>HttpContext</c>. Quando null, a global query filter de
    /// <see cref="Membership"/> degrada para no-op.
    /// </summary>
    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<EcbSnapshotState> EcbSnapshotStates => Set<EcbSnapshotState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("shared");

        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("Tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.Name).IsRequired().HasMaxLength(200);
            b.Property(t => t.PrimaryCurrency)
                .IsRequired()
                .HasMaxLength(3)
                .HasDefaultValue("EUR");
            b.Property(t => t.CreatedAt).IsRequired();
            b.Property(t => t.UpdatedAt).IsRequired();
            b.Property(t => t.Version).IsConcurrencyToken();
            b.HasIndex(t => t.Name);
        });

        modelBuilder.Entity<Currency>(b =>
        {
            b.ToTable("currencies");
            b.HasKey(c => c.Code);
            b.Property(c => c.Code)
                .HasColumnName("code")
                .HasMaxLength(3)
                .IsRequired();
            b.Property(c => c.Name)
                .HasColumnName("name")
                .HasMaxLength(120)
                .IsRequired();
            b.Property(c => c.Symbol)
                .HasColumnName("symbol")
                .HasMaxLength(16)
                .IsRequired();
            b.Property(c => c.MinorUnits)
                .HasColumnName("minor_units")
                .IsRequired();
            b.Property(c => c.IsActive)
                .HasColumnName("is_active")
                .IsRequired();
            b.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
            b.Property(c => c.Version).HasColumnName("version").IsConcurrencyToken();

            b.HasIndex(c => c.IsActive);
        });

        modelBuilder.Entity<ExchangeRate>(b =>
        {
            b.ToTable("exchange_rates");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasColumnName("id");
            b.Property(r => r.RateDate).HasColumnName("rate_date").IsRequired();
            b.Property(r => r.FromCurrency)
                .HasColumnName("from_currency")
                .HasMaxLength(3)
                .IsRequired();
            b.Property(r => r.ToCurrency)
                .HasColumnName("to_currency")
                .HasMaxLength(3)
                .IsRequired();
            b.Property(r => r.Rate)
                .HasColumnName("rate")
                .HasPrecision(20, 8)
                .IsRequired();
            b.Property(r => r.Source)
                .HasColumnName("source")
                .HasMaxLength(16)
                .IsRequired();
            b.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(r => r.UpdatedAt).HasColumnName("updated_at").IsRequired();
            b.Property(r => r.Version).HasColumnName("version").IsConcurrencyToken();

            b.HasIndex(r => new { r.RateDate, r.FromCurrency, r.ToCurrency }).IsUnique();
            b.HasIndex(r => r.RateDate);
            b.HasOne<Currency>()
                .WithMany()
                .HasForeignKey(r => r.ToCurrency)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EcbSnapshotState>(b =>
        {
            b.ToTable("ecb_snapshot_state");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();
            b.Property(s => s.LastRunAt).HasColumnName("last_run_at");
            b.Property(s => s.LastSuccessAt).HasColumnName("last_success_at");
            b.Property(s => s.LastError)
                .HasColumnName("last_error")
                .HasMaxLength(EcbSnapshotState.LastErrorMaxLength);
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
