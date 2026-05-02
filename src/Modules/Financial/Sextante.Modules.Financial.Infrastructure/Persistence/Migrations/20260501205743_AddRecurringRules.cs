using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recurring_rule_id",
                schema: "financial",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "recurring_rules",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    frequency = table.Column<string>(type: "text", nullable: false),
                    interval = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    next_occurrence = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_recurring_rule_id",
                schema: "financial",
                table: "transactions",
                column: "recurring_rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_rules_tenant_id",
                schema: "financial",
                table: "recurring_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_rules_tenant_id_next_occurrence",
                schema: "financial",
                table: "recurring_rules",
                columns: new[] { "tenant_id", "next_occurrence" },
                filter: "next_occurrence IS NOT NULL AND is_active = true AND deleted_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_recurring_rules_recurring_rule_id",
                schema: "financial",
                table: "transactions",
                column: "recurring_rule_id",
                principalSchema: "financial",
                principalTable: "recurring_rules",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // RLS + FORCE na nova tabela, com policy por tenant_id
            // baseada em current_setting('app.current_tenant_id').
            // Segue o mesmo padrão da migration EnableFinancialRowLevelSecurity.
            migrationBuilder.Sql("""
                ALTER TABLE financial.recurring_rules ENABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.recurring_rules FORCE ROW LEVEL SECURITY;
                CREATE POLICY recurring_rules_tenant_isolation ON financial.recurring_rules
                    USING (tenant_id = current_setting('app.current_tenant_id', false)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant_id', false)::uuid);
                """);

            // Sentinel guard — defesa em profundidade.
            migrationBuilder.Sql("""
                ALTER TABLE financial.recurring_rules
                    ADD CONSTRAINT recurring_rules_tenant_id_not_sentinel
                    CHECK (tenant_id <> 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid);
                """);

            // Grants para sextante_app na nova tabela + default futuras.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
                        GRANT SELECT, INSERT, UPDATE, DELETE ON financial.recurring_rules TO sextante_app;
                    END IF;
                END
                $$;
                """);

            // Índice partial para lookup de Transaction por recurring_rule_id
            // (filtro no Dashboard "Ver transações").
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_transactions_recurring_rule_id_notnull
                    ON financial.transactions (recurring_rule_id)
                    WHERE recurring_rule_id IS NOT NULL;
                """);

            // Índice unique partial — defesa contra race condition em multi-worker.
            // Garante idempotência a nível de DB: um (recurring_rule_id, ocorrência)
            // só pode ter uma Transaction.
            // `occurred_at` é timestamptz; `date_trunc(text, timestamptz)` é STABLE
            // (depende da TIMEZONE da sessão) e Postgres rejeita-a em índices.
            // `AT TIME ZONE 'UTC'` converte para timestamp sem TZ — `date_trunc(text,
            // timestamp)` é IMMUTABLE. O materializer insere sempre OccurredAt a 00:00 UTC,
            // por isso o trunc devolve a data original.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS uq_transactions_recurring_occurrence
                    ON financial.transactions (recurring_rule_id, date_trunc('day', occurred_at AT TIME ZONE 'UTC'))
                    WHERE recurring_rule_id IS NOT NULL AND deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS uq_transactions_recurring_occurrence;
                DROP INDEX IF EXISTS ix_transactions_recurring_rule_id_notnull;
                """);

            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS recurring_rules_tenant_isolation ON financial.recurring_rules;
                ALTER TABLE financial.recurring_rules DISABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.recurring_rules DROP CONSTRAINT IF EXISTS recurring_rules_tenant_id_not_sentinel;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_transactions_recurring_rules_recurring_rule_id",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "recurring_rules",
                schema: "financial");

            migrationBuilder.DropIndex(
                name: "IX_transactions_recurring_rule_id",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "recurring_rule_id",
                schema: "financial",
                table: "transactions");
        }
    }
}
