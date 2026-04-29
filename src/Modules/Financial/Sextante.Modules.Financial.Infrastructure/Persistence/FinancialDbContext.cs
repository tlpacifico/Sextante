using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Infrastructure.Persistence;

public sealed class FinancialDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    /// <summary>
    /// <paramref name="tenantContext"/> é nullable para permitir a
    /// configuração de design-time/migration runner sem dependência de
    /// <c>HttpContext</c>. Em runtime, DI fornece o scoped tenant.
    /// </summary>
    public FinancialDbContext(
        DbContextOptions<FinancialDbContext> options,
        ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("financial");

        modelBuilder.Entity<Account>(b =>
        {
            b.ToTable("accounts");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).HasColumnName("id");
            b.Property(a => a.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .IsRequired();
            b.Property(a => a.Name)
                .HasColumnName("name")
                .IsRequired()
                .HasMaxLength(Account.NameMaxLength);
            b.Property(a => a.Type)
                .HasColumnName("type")
                .HasConversion<int>()
                .IsRequired();
            b.Property(a => a.Currency)
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsRequired();
            b.OwnsOne(a => a.OpeningBalance, money =>
            {
                money.Property(m => m.Amount)
                    .HasColumnName("opening_balance_amount")
                    .HasPrecision(20, 8)
                    .IsRequired();
                money.Property(m => m.Currency)
                    .HasColumnName("opening_balance_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });
            b.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(a => a.UpdatedAt).HasColumnName("updated_at").IsRequired();
            b.Property(a => a.DeletedAt).HasColumnName("deleted_at");
            b.Property(a => a.Version).HasColumnName("version").IsConcurrencyToken();

            b.HasIndex(a => a.TenantId);
            b.HasIndex(a => new { a.TenantId, a.DeletedAt });

            b.HasQueryFilter(a =>
                a.DeletedAt == null
                && (_tenantContext == null || a.TenantId == _tenantContext.TenantId));
        });

        modelBuilder.Entity<Category>(b =>
        {
            b.ToTable("categories");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasColumnName("id");
            b.Property(c => c.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .IsRequired();
            b.Property(c => c.Name)
                .HasColumnName("name")
                .IsRequired()
                .HasMaxLength(Category.NameMaxLength);
            b.Property(c => c.Kind)
                .HasColumnName("kind")
                .HasConversion<int>()
                .IsRequired();
            b.Property(c => c.IconName)
                .HasColumnName("icon_name")
                .IsRequired()
                .HasMaxLength(64);
            b.Property(c => c.ColorHex)
                .HasColumnName("color_hex")
                .IsRequired()
                .HasMaxLength(7);
            b.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
            b.Property(c => c.DeletedAt).HasColumnName("deleted_at");
            b.Property(c => c.Version).HasColumnName("version").IsConcurrencyToken();

            b.HasIndex(c => c.TenantId);
            b.HasIndex(c => new { c.TenantId, c.Kind, c.DeletedAt });

            b.HasQueryFilter(c =>
                c.DeletedAt == null
                && (_tenantContext == null || c.TenantId == _tenantContext.TenantId));
        });

        modelBuilder.Entity<Transaction>(b =>
        {
            b.ToTable("transactions");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).HasColumnName("id");
            b.Property(t => t.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .IsRequired();
            b.Property(t => t.AccountId).HasColumnName("account_id").IsRequired();
            b.Property(t => t.CategoryId).HasColumnName("category_id").IsRequired();
            b.Property(t => t.OccurredAt).HasColumnName("occurred_at").IsRequired();
            b.OwnsOne(t => t.Amount, money =>
            {
                money.Property(m => m.Amount)
                    .HasColumnName("amount")
                    .HasPrecision(20, 8)
                    .IsRequired();
                money.Property(m => m.Currency)
                    .HasColumnName("currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });
            b.Property(t => t.Description)
                .HasColumnName("description")
                .HasMaxLength(Transaction.DescriptionMaxLength);
            b.Property(t => t.ExchangeRateToPrimary)
                .HasColumnName("exchange_rate_to_primary")
                .HasPrecision(20, 8);
            b.Property(t => t.ExchangeRateAt)
                .HasColumnName("exchange_rate_at");

            // Tags como jsonb. Backing field via Metadata API porque a
            // propriedade é exposta como IReadOnlyList<string> mas o EF
            // precisa de mapear contra o List<string> privado.
            var tagsProperty = b.Metadata.AddProperty(
                "_tags",
                typeof(List<string>),
                typeof(Transaction).GetField("_tags",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!);
            tagsProperty.SetField("_tags");
            tagsProperty.SetColumnName("tags");
            tagsProperty.SetColumnType("jsonb");
            tagsProperty.SetValueConverter(new TagsJsonConverter());
            tagsProperty.IsNullable = false;

            b.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
            b.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();
            b.Property(t => t.DeletedAt).HasColumnName("deleted_at");
            b.Property(t => t.Version).HasColumnName("version").IsConcurrencyToken();

            b.Ignore(t => t.Tags);

            b.HasIndex(t => t.TenantId);
            b.HasIndex(t => new { t.TenantId, t.OccurredAt });
            b.HasIndex(t => new { t.TenantId, t.CategoryId, t.DeletedAt });
            b.HasIndex(t => new { t.TenantId, t.AccountId, t.DeletedAt });

            b.HasQueryFilter(t =>
                t.DeletedAt == null
                && (_tenantContext == null || t.TenantId == _tenantContext.TenantId));
        });
    }
}
