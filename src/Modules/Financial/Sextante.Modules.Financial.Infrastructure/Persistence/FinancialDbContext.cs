using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.CategorizationRules;
using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.Modules.Financial.Domain.InstallmentPlans;
using Sextante.Modules.Financial.Domain.ImportBatches;
using Sextante.Modules.Financial.Domain.RecurringRules;
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
    public DbSet<CategorizationRule> CategorizationRules => Set<CategorizationRule>();
    public DbSet<ImportProfile> ImportProfiles => Set<ImportProfile>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<RecurringRule> RecurringRules => Set<RecurringRule>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<InstallmentPlan> InstallmentPlans => Set<InstallmentPlan>();
    public DbSet<BudgetAlert> BudgetAlerts => Set<BudgetAlert>();

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
            b.Property(a => a.OpeningBalanceDate)
                .HasColumnName("opening_balance_date")
                .IsRequired();
            // Phase 6.5 grupo 5 — definições do cartão, nullable (só CreditCard).
            b.OwnsOne(a => a.CreditCard, cc =>
            {
                cc.Property(c => c.CreditLimitAmount)
                    .HasColumnName("credit_card_limit_amount")
                    .HasPrecision(20, 8);
                cc.Property(c => c.CreditLimitCurrency)
                    .HasColumnName("credit_card_limit_currency")
                    .HasMaxLength(3);
                cc.Property(c => c.StatementClosingDay)
                    .HasColumnName("credit_card_statement_closing_day")
                    .HasColumnType("smallint");
                cc.Property(c => c.PaymentDueDay)
                    .HasColumnName("credit_card_payment_due_day")
                    .HasColumnType("smallint");
                cc.Property(c => c.PaymentAccountId)
                    .HasColumnName("credit_card_payment_account_id");
                cc.Ignore(c => c.CreditLimit);
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
            // Phase 6.5 — nullable: transferências, acertos e transações
            // regulares ainda sem categoria (ADR-014).
            b.Property(t => t.CategoryId).HasColumnName("category_id");
            b.Property(t => t.Direction).HasColumnName("direction").HasConversion<short>().IsRequired();
            b.Property(t => t.Kind).HasColumnName("kind").HasConversion<short>().IsRequired();
            b.Property(t => t.TransferId).HasColumnName("transfer_id");
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
            b.HasIndex(t => new { t.TenantId, t.TransferId })
                .HasFilter("transfer_id IS NOT NULL")
                .HasDatabaseName("ix_transactions_tenant_transfer");

            b.HasQueryFilter(t =>
                t.DeletedAt == null
                && (_tenantContext == null || t.TenantId == _tenantContext.TenantId));
        });

        // Transaction — Phase 4 audit columns
        modelBuilder.Entity<Transaction>(cfg =>
        {
            cfg.Property(t => t.CategorizationRuleId)
                .HasColumnName("categorization_rule_id")
                .HasColumnType("uuid")
                .IsRequired(false);
            cfg.Property(t => t.CategorizedAt)
                .HasColumnName("categorized_at")
                .HasColumnType("timestamptz")
                .IsRequired(false);
            cfg.HasOne<CategorizationRule>()
                .WithMany()
                .HasForeignKey(t => t.CategorizationRuleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Transaction — Phase 5a recurring audit column
        modelBuilder.Entity<Transaction>(cfg =>
        {
            cfg.Property(t => t.RecurringRuleId)
                .HasColumnName("recurring_rule_id")
                .HasColumnType("uuid")
                .IsRequired(false);
            cfg.HasOne<RecurringRule>()
                .WithMany()
                .HasForeignKey(t => t.RecurringRuleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // RecurringRule
        modelBuilder.Entity<RecurringRule>(cfg =>
        {
            cfg.ToTable("recurring_rules");
            cfg.HasKey(r => r.Id);
            cfg.Property(r => r.Id).HasColumnName("id");
            cfg.Property(r => r.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .HasColumnType("uuid");
            cfg.Property(r => r.Description)
                .HasColumnName("description")
                .HasMaxLength(RecurringRule.DescriptionMaxLength)
                .IsRequired();
            cfg.OwnsOne(r => r.Amount, money =>
            {
                money.Property(m => m.Amount)
                    .HasColumnName("amount_amount")
                    .HasPrecision(20, 8)
                    .IsRequired();
                money.Property(m => m.Currency)
                    .HasColumnName("amount_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });
            cfg.Property(r => r.AccountId).HasColumnName("account_id").HasColumnType("uuid").IsRequired();
            cfg.Property(r => r.CategoryId).HasColumnName("category_id").HasColumnType("uuid").IsRequired(false);
            cfg.Property(r => r.Frequency)
                .HasColumnName("frequency")
                .HasConversion<string>()
                .IsRequired();
            cfg.Property(r => r.Interval).HasColumnName("interval").IsRequired().HasDefaultValue(1);
            cfg.Property(r => r.StartDate).HasColumnName("start_date").HasColumnType("date").IsRequired();
            cfg.Property(r => r.EndDate).HasColumnName("end_date").HasColumnType("date").IsRequired(false);
            cfg.Property(r => r.NextOccurrence).HasColumnName("next_occurrence").HasColumnType("date").IsRequired(false);
            cfg.Property(r => r.IsActive).HasColumnName("is_active").IsRequired().HasDefaultValue(true);

            var tagsProperty = cfg.Metadata.AddProperty(
                "_tags",
                typeof(List<string>),
                typeof(RecurringRule).GetField("_tags",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!);
            tagsProperty.SetField("_tags");
            tagsProperty.SetColumnName("tags");
            tagsProperty.SetColumnType("jsonb");
            tagsProperty.SetValueConverter(new TagsJsonConverter());
            tagsProperty.IsNullable = false;

            cfg.Ignore(r => r.Tags);

            cfg.Property(r => r.Version).HasColumnName("version").IsConcurrencyToken();
            cfg.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
            cfg.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
            cfg.Property(r => r.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");

            cfg.HasIndex(r => r.TenantId);
            cfg.HasIndex(r => new { r.TenantId, r.NextOccurrence })
                .HasFilter("next_occurrence IS NOT NULL AND is_active = true AND deleted_at IS NULL");

            cfg.HasQueryFilter(r => r.DeletedAt == null
                && (_tenantContext == null || r.TenantId == _tenantContext.TenantId));
        });

        // CategorizationRule
        modelBuilder.Entity<CategorizationRule>(cfg =>
        {
            cfg.ToTable("categorization_rules");
            cfg.HasKey(r => r.Id);
            cfg.Property(r => r.Id).HasColumnName("id");
            cfg.Property(r => r.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .HasColumnType("uuid");
            cfg.Property(r => r.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            cfg.Property(r => r.Pattern).HasColumnName("pattern").HasMaxLength(512).IsRequired();
            cfg.Property(r => r.MatchType).HasColumnName("match_type").HasConversion<string>().IsRequired();
            cfg.Property(r => r.CategoryId).HasColumnName("category_id").HasColumnType("uuid").IsRequired();
            cfg.Property(r => r.Priority).HasColumnName("priority").IsRequired();
            cfg.Property(r => r.IsActive).HasColumnName("is_active").IsRequired();
            cfg.Property(r => r.Version).HasColumnName("version").IsConcurrencyToken();
            cfg.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
            cfg.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
            cfg.Property(r => r.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
            cfg.HasIndex(r => new { r.TenantId, r.Priority });
            cfg.HasQueryFilter(r => r.DeletedAt == null
                && (_tenantContext == null || r.TenantId == _tenantContext.TenantId));
        });

        // ImportProfile
        modelBuilder.Entity<ImportProfile>(cfg =>
        {
            cfg.ToTable("import_profiles");
            cfg.HasKey(p => p.Id);
            cfg.Property(p => p.Id).HasColumnName("id");
            cfg.Property(p => p.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .HasColumnType("uuid");
            cfg.Property(p => p.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
            cfg.Property(p => p.Delimiter).HasColumnName("delimiter").HasMaxLength(1).IsRequired();
            cfg.Property(p => p.HasHeaderRow).HasColumnName("has_header_row").IsRequired();
            cfg.Property(p => p.DateFormat).HasColumnName("date_format").HasMaxLength(32).IsRequired();
            cfg.Property(p => p.DecimalSeparator).HasColumnName("decimal_separator").HasMaxLength(1).IsRequired();
            cfg.Property(p => p.SkipRows).HasColumnName("skip_rows").IsRequired();
            cfg.Property(p => p.Version).HasColumnName("version").IsConcurrencyToken();
            cfg.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
            cfg.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
            cfg.Property(p => p.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
            cfg.Property(p => p.ColumnMappings)
                .HasColumnName("column_mappings")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<List<ColumnMapping>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<ColumnMapping>())
                .HasColumnType("jsonb");
            cfg.HasQueryFilter(p => p.DeletedAt == null
                && (_tenantContext == null || p.TenantId == _tenantContext.TenantId));
        });

        // Budget — Phase 5b
        modelBuilder.Entity<Budget>(cfg =>
        {
            cfg.ToTable("budgets");
            cfg.HasKey(b => b.Id);
            cfg.Property(b => b.Id).HasColumnName("id");
            cfg.Property(b => b.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .HasColumnType("uuid")
                .IsRequired();
            cfg.Property(b => b.CategoryId).HasColumnName("category_id").HasColumnType("uuid").IsRequired();
            cfg.OwnsOne(b => b.Period, period =>
            {
                period.Property(p => p.Year)
                    .HasColumnName("period_year")
                    .IsRequired();
                period.Property(p => p.Month)
                    .HasColumnName("period_month")
                    .IsRequired();
            });
            cfg.Navigation(b => b.Period).IsRequired();
            cfg.OwnsOne(b => b.Limit, money =>
            {
                money.Property(m => m.Amount)
                    .HasColumnName("limit_amount")
                    .HasPrecision(20, 8)
                    .IsRequired();
                money.Property(m => m.Currency)
                    .HasColumnName("limit_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });
            cfg.Property(b => b.AlertThresholdPercent)
                .HasColumnName("alert_threshold_percent")
                .HasDefaultValue(Budget.DefaultThreshold)
                .IsRequired();
            cfg.Property(b => b.Notes)
                .HasColumnName("notes")
                .HasMaxLength(Budget.NotesMaxLength)
                .IsRequired(false);

            cfg.Property(b => b.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            cfg.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
            cfg.Property(b => b.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
            cfg.Property(b => b.Version).HasColumnName("version").IsConcurrencyToken();

            cfg.HasIndex(b => b.TenantId);
            cfg.HasOne<Category>()
                .WithMany()
                .HasForeignKey(b => b.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            cfg.HasQueryFilter(b => b.DeletedAt == null
                && (_tenantContext == null || b.TenantId == _tenantContext.TenantId));
        });

        // InstallmentPlan — Phase 6.5 grupo 6. Conta e compra são soft
        // references (sem FK), validadas na Application.
        modelBuilder.Entity<InstallmentPlan>(cfg =>
        {
            cfg.ToTable("installment_plans");
            cfg.HasKey(p => p.Id);
            cfg.Property(p => p.Id).HasColumnName("id");
            cfg.Property(p => p.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .HasColumnType("uuid")
                .IsRequired();
            cfg.Property(p => p.AccountId).HasColumnName("account_id").HasColumnType("uuid").IsRequired();
            cfg.Property(p => p.PurchaseTransactionId).HasColumnName("purchase_transaction_id").HasColumnType("uuid");
            cfg.Property(p => p.Description)
                .HasColumnName("description")
                .HasMaxLength(InstallmentPlan.DescriptionMaxLength)
                .IsRequired();
            cfg.OwnsOne(p => p.TotalAmount, money =>
            {
                money.Property(m => m.Amount)
                    .HasColumnName("total_amount")
                    .HasPrecision(20, 8)
                    .IsRequired();
                money.Property(m => m.Currency)
                    .HasColumnName("total_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });
            cfg.Navigation(p => p.TotalAmount).IsRequired();
            cfg.Property(p => p.InstallmentCount).HasColumnName("installment_count").IsRequired();
            cfg.Property(p => p.InstallmentsAlreadyPaid).HasColumnName("installments_already_paid").IsRequired();
            cfg.Property(p => p.FirstInstallmentDate).HasColumnName("first_installment_date").IsRequired();
            cfg.Property(p => p.AnnualRate).HasColumnName("annual_rate").HasPrecision(7, 4);

            cfg.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            cfg.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
            cfg.Property(p => p.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
            cfg.Property(p => p.Version).HasColumnName("version").IsConcurrencyToken();

            cfg.HasIndex(p => p.TenantId);
            cfg.HasIndex(p => new { p.TenantId, p.AccountId });

            cfg.HasQueryFilter(p => p.DeletedAt == null
                && (_tenantContext == null || p.TenantId == _tenantContext.TenantId));
        });

        // BudgetAlert — Phase 5b
        modelBuilder.Entity<BudgetAlert>(cfg =>
        {
            cfg.ToTable("budget_alerts");
            cfg.HasKey(a => a.Id);
            cfg.Property(a => a.Id).HasColumnName("id");
            cfg.Property(a => a.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .HasColumnType("uuid")
                .IsRequired();
            cfg.Property(a => a.BudgetId).HasColumnName("budget_id").HasColumnType("uuid").IsRequired();
            cfg.Property(a => a.Threshold).HasColumnName("threshold").IsRequired();
            cfg.Property(a => a.TriggeredAt).HasColumnName("triggered_at").HasColumnType("timestamptz").IsRequired();
            cfg.OwnsOne(a => a.SpentAtTrigger, money =>
            {
                money.Property(m => m.Amount)
                    .HasColumnName("spent_at_trigger_amount")
                    .HasPrecision(20, 8)
                    .IsRequired();
                money.Property(m => m.Currency)
                    .HasColumnName("spent_at_trigger_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });
            cfg.Property(a => a.Acknowledged).HasColumnName("acknowledged").HasDefaultValue(false).IsRequired();
            cfg.Property(a => a.AcknowledgedAt).HasColumnName("acknowledged_at").HasColumnType("timestamptz").IsRequired(false);
            cfg.Property(a => a.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
            cfg.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
            cfg.Property(a => a.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
            cfg.Property(a => a.Version).HasColumnName("version").IsConcurrencyToken();

            cfg.HasIndex(a => a.TenantId);
            cfg.HasOne<Budget>()
                .WithMany()
                .HasForeignKey(a => a.BudgetId)
                .OnDelete(DeleteBehavior.Cascade);

            cfg.HasQueryFilter(a => a.DeletedAt == null
                && (_tenantContext == null || a.TenantId == _tenantContext.TenantId));
        });

        // ImportBatch
        modelBuilder.Entity<ImportBatch>(cfg =>
        {
            cfg.ToTable("import_batches");
            cfg.HasKey(b => b.Id);
            cfg.Property(b => b.Id).HasColumnName("id");
            cfg.Property(b => b.TenantId)
                .HasConversion(v => v.Value, v => new TenantId(v))
                .HasColumnName("tenant_id")
                .HasColumnType("uuid");
            cfg.Property(b => b.ImportProfileId).HasColumnName("import_profile_id").HasColumnType("uuid").IsRequired(false);
            cfg.Property(b => b.FileName).HasColumnName("file_name").HasMaxLength(256).IsRequired();
            cfg.Property(b => b.Status).HasColumnName("status").HasConversion<string>().IsRequired();
            cfg.Property(b => b.TotalRows).HasColumnName("total_rows").IsRequired();
            cfg.Property(b => b.ImportedRows).HasColumnName("imported_rows").IsRequired();
            cfg.Property(b => b.DuplicateRows).HasColumnName("duplicate_rows").IsRequired();
            cfg.Property(b => b.ErrorRows).HasColumnName("error_rows").IsRequired();
            cfg.Property(b => b.ParsedPreviewJson).HasColumnName("parsed_preview").HasColumnType("jsonb").IsRequired(false);
            cfg.Property(b => b.CategorizationResultJson).HasColumnName("categorization_result").HasColumnType("jsonb").IsRequired(false);
            cfg.Property(b => b.PreviewTruncated).HasColumnName("preview_truncated").IsRequired();
            cfg.Property(b => b.Version).HasColumnName("version").IsConcurrencyToken();
            cfg.Property(b => b.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
            cfg.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz");
            cfg.Property(b => b.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
            cfg.HasQueryFilter(b => b.DeletedAt == null
                && (_tenantContext == null || b.TenantId == _tenantContext.TenantId));
        });
    }
}