using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 6.5 §6.2 — tabela <c>financial.installment_plans</c> (planos de
    /// prestações de cartões), com o padrão completo das tabelas tenant-owned
    /// (AddBudgetsAndAlerts): RLS + FORCE, sentinel CHECK, grants ao
    /// <c>sextante_app</c> e CHECKs de domínio. Conta e compra são soft
    /// references (sem FK); uma compra só pode ter um plano ativo.
    /// </summary>
    public partial class AddInstallmentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installment_plans",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    installment_count = table.Column<int>(type: "integer", nullable: false),
                    installments_already_paid = table.Column<int>(type: "integer", nullable: false),
                    first_installment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    annual_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installment_plans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_installment_plans_tenant_id",
                schema: "financial",
                table: "installment_plans",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_installment_plans_tenant_id_account_id",
                schema: "financial",
                table: "installment_plans",
                columns: new[] { "tenant_id", "account_id" });

            // CHECKs de domínio (defesa em profundidade — o Domain já valida)
            // e sentinel contra o GUC default do pool.
            migrationBuilder.Sql("""
                ALTER TABLE financial.installment_plans
                    ADD CONSTRAINT chk_installment_plans_count
                    CHECK (installment_count BETWEEN 2 AND 120),
                    ADD CONSTRAINT chk_installment_plans_already_paid
                    CHECK (installments_already_paid >= 0 AND installments_already_paid < installment_count),
                    ADD CONSTRAINT chk_installment_plans_total_positive
                    CHECK (total_amount > 0),
                    ADD CONSTRAINT chk_installment_plans_annual_rate
                    CHECK (annual_rate IS NULL OR annual_rate BETWEEN 0 AND 100),
                    ADD CONSTRAINT installment_plans_tenant_id_not_sentinel
                    CHECK (tenant_id <> 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid);
                """);

            migrationBuilder.Sql("""
                ALTER TABLE financial.installment_plans ENABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.installment_plans FORCE ROW LEVEL SECURITY;
                CREATE POLICY installment_plans_tenant_isolation ON financial.installment_plans
                    USING (tenant_id = current_setting('app.current_tenant_id', false)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant_id', false)::uuid);
                """);

            // Grants para sextante_app (NOBYPASSRLS).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON financial.installment_plans TO sextante_app;
                    END IF;
                END
                $$;
                """);

            // Uma compra só tem um plano ativo (soft-deleted excluídos).
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS uq_installment_plans_tenant_purchase
                    ON financial.installment_plans (tenant_id, purchase_transaction_id)
                    WHERE purchase_transaction_id IS NOT NULL AND deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS financial.uq_installment_plans_tenant_purchase;
                DROP POLICY IF EXISTS installment_plans_tenant_isolation ON financial.installment_plans;
                ALTER TABLE financial.installment_plans DISABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.installment_plans DROP CONSTRAINT IF EXISTS installment_plans_tenant_id_not_sentinel;
                ALTER TABLE financial.installment_plans DROP CONSTRAINT IF EXISTS chk_installment_plans_annual_rate;
                ALTER TABLE financial.installment_plans DROP CONSTRAINT IF EXISTS chk_installment_plans_total_positive;
                ALTER TABLE financial.installment_plans DROP CONSTRAINT IF EXISTS chk_installment_plans_already_paid;
                ALTER TABLE financial.installment_plans DROP CONSTRAINT IF EXISTS chk_installment_plans_count;
                """);

            migrationBuilder.DropTable(
                name: "installment_plans",
                schema: "financial");
        }
    }
}
