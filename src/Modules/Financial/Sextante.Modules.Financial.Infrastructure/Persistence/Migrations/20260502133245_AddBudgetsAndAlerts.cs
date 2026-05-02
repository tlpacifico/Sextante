using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetsAndAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budgets",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_year = table.Column<int>(type: "integer", nullable: false),
                    period_month = table.Column<int>(type: "integer", nullable: false),
                    limit_amount = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    limit_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    alert_threshold_percent = table.Column<int>(type: "integer", nullable: false, defaultValue: 80),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budgets", x => x.id);
                    table.ForeignKey(
                        name: "FK_budgets_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "financial",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "budget_alerts",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    threshold = table.Column<int>(type: "integer", nullable: false),
                    triggered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    spent_at_trigger_amount = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    spent_at_trigger_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    acknowledged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_alerts", x => x.id);
                    table.ForeignKey(
                        name: "FK_budget_alerts_budgets_budget_id",
                        column: x => x.budget_id,
                        principalSchema: "financial",
                        principalTable: "budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_budget_alerts_budget_id",
                schema: "financial",
                table: "budget_alerts",
                column: "budget_id");

            migrationBuilder.CreateIndex(
                name: "IX_budget_alerts_tenant_id",
                schema: "financial",
                table: "budget_alerts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_budgets_category_id",
                schema: "financial",
                table: "budgets",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_budgets_tenant_id",
                schema: "financial",
                table: "budgets",
                column: "tenant_id");

            // Range checks no nível DB (defesa em profundidade — Domain
            // já valida).
            migrationBuilder.Sql("""
                ALTER TABLE financial.budgets
                    ADD CONSTRAINT chk_budgets_period_year
                    CHECK (period_year BETWEEN 2000 AND 2100);
                ALTER TABLE financial.budgets
                    ADD CONSTRAINT chk_budgets_period_month
                    CHECK (period_month BETWEEN 1 AND 12);
                ALTER TABLE financial.budgets
                    ADD CONSTRAINT chk_budgets_limit_positive
                    CHECK (limit_amount > 0);
                ALTER TABLE financial.budgets
                    ADD CONSTRAINT chk_budgets_threshold_range
                    CHECK (alert_threshold_percent BETWEEN 1 AND 99);
                ALTER TABLE financial.budget_alerts
                    ADD CONSTRAINT chk_budget_alerts_threshold_range
                    CHECK (threshold BETWEEN 1 AND 100);
                """);

            // RLS + FORCE em ambas as tabelas. Mesmo padrão da
            // EnableFinancialRowLevelSecurity migration (Phase 1a).
            migrationBuilder.Sql("""
                ALTER TABLE financial.budgets ENABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.budgets FORCE ROW LEVEL SECURITY;
                CREATE POLICY budgets_tenant_isolation ON financial.budgets
                    USING (tenant_id = current_setting('app.current_tenant_id', false)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant_id', false)::uuid);

                ALTER TABLE financial.budget_alerts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.budget_alerts FORCE ROW LEVEL SECURITY;
                CREATE POLICY budget_alerts_tenant_isolation ON financial.budget_alerts
                    USING (tenant_id = current_setting('app.current_tenant_id', false)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant_id', false)::uuid);
                """);

            // Sentinel guard — defesa em profundidade contra leak do
            // GUC default 'ffffffff-...' do checkout do pool.
            migrationBuilder.Sql("""
                ALTER TABLE financial.budgets
                    ADD CONSTRAINT budgets_tenant_id_not_sentinel
                    CHECK (tenant_id <> 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid);
                ALTER TABLE financial.budget_alerts
                    ADD CONSTRAINT budget_alerts_tenant_id_not_sentinel
                    CHECK (tenant_id <> 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid);
                """);

            // Grants para sextante_app (NOBYPASSRLS).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON financial.budgets TO sextante_app;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON financial.budget_alerts TO sextante_app;
                    END IF;
                END
                $$;
                """);

            // Unique partial index — 1 budget ativo por
            // (tenant, category, year, month). Exclui soft-deleted.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS uq_budgets_tenant_category_period
                    ON financial.budgets (tenant_id, category_id, period_year, period_month)
                    WHERE deleted_at IS NULL;
                """);

            // Unique partial index — 1 alerta por (tenant, budget, threshold).
            // Garante idempotência do dispatch handler.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS uq_budget_alerts_tenant_budget_threshold
                    ON financial.budget_alerts (tenant_id, budget_id, threshold)
                    WHERE deleted_at IS NULL;
                """);

            // Index parcial para query do banner (active alerts).
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_budget_alerts_tenant_active
                    ON financial.budget_alerts (tenant_id, triggered_at DESC)
                    WHERE acknowledged = false AND deleted_at IS NULL;
                """);

            // Index parcial para listagem por (tenant, year, month).
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_budgets_tenant_period
                    ON financial.budgets (tenant_id, period_year, period_month)
                    WHERE deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS financial.ix_budgets_tenant_period;
                DROP INDEX IF EXISTS financial.ix_budget_alerts_tenant_active;
                DROP INDEX IF EXISTS financial.uq_budget_alerts_tenant_budget_threshold;
                DROP INDEX IF EXISTS financial.uq_budgets_tenant_category_period;
                """);

            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS budget_alerts_tenant_isolation ON financial.budget_alerts;
                DROP POLICY IF EXISTS budgets_tenant_isolation ON financial.budgets;
                ALTER TABLE financial.budget_alerts DISABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.budgets DISABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.budget_alerts DROP CONSTRAINT IF EXISTS budget_alerts_tenant_id_not_sentinel;
                ALTER TABLE financial.budgets DROP CONSTRAINT IF EXISTS budgets_tenant_id_not_sentinel;
                """);

            migrationBuilder.DropTable(
                name: "budget_alerts",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "budgets",
                schema: "financial");
        }
    }
}
