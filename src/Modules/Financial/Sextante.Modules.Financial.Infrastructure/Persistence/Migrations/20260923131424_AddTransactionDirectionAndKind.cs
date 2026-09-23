using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 6.5 §1.2 (ADR-014) — a direção da transação passa a ser coluna
    /// em vez de vir de <c>categories.kind</c> em tempo de leitura; entra o
    /// tipo (Regular/Transfer/Adjustment) e o <c>transfer_id</c> que liga as
    /// pernas de uma transferência. <c>category_id</c> fica nullable e o
    /// sentinel <c>Guid.Empty</c> do materializer (Phase 5a) passa a NULL.
    /// </summary>
    public partial class AddTransactionDirectionAndKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "category_id",
                schema: "financial",
                table: "transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Default temporário = saída (1): as linhas existentes ficam
            // coerentes mesmo antes do backfill; o default sai no fim.
            migrationBuilder.AddColumn<short>(
                name: "direction",
                schema: "financial",
                table: "transactions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<short>(
                name: "kind",
                schema: "financial",
                table: "transactions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_id",
                schema: "financial",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_transactions_tenant_transfer",
                schema: "financial",
                table: "transactions",
                columns: new[] { "tenant_id", "transfer_id" },
                filter: "transfer_id IS NOT NULL");

            migrationBuilder.Sql(TransactionDirectionBackfill.Sql);

            migrationBuilder.Sql("""
                ALTER TABLE financial.transactions ALTER COLUMN direction DROP DEFAULT;
                ALTER TABLE financial.transactions
                    ADD CONSTRAINT chk_transactions_direction CHECK (direction IN (0, 1));
                ALTER TABLE financial.transactions
                    ADD CONSTRAINT chk_transactions_kind CHECK (kind IN (0, 1, 2));
                ALTER TABLE financial.transactions
                    ADD CONSTRAINT chk_transactions_category_only_regular
                    CHECK (kind = 0 OR category_id IS NULL);
                ALTER TABLE financial.transactions
                    ADD CONSTRAINT chk_transactions_transfer_id
                    CHECK ((kind = 1) = (transfer_id IS NOT NULL));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE financial.transactions DROP CONSTRAINT IF EXISTS chk_transactions_transfer_id;
                ALTER TABLE financial.transactions DROP CONSTRAINT IF EXISTS chk_transactions_category_only_regular;
                ALTER TABLE financial.transactions DROP CONSTRAINT IF EXISTS chk_transactions_kind;
                ALTER TABLE financial.transactions DROP CONSTRAINT IF EXISTS chk_transactions_direction;
                UPDATE financial.transactions
                   SET category_id = '00000000-0000-0000-0000-000000000000'
                 WHERE category_id IS NULL;
                """);

            migrationBuilder.DropIndex(
                name: "ix_transactions_tenant_transfer",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "direction",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "transfer_id",
                schema: "financial",
                table: "transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "category_id",
                schema: "financial",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }

    /// <summary>
    /// Backfill da direção. Idempotente — corre na migration e nos testes.
    /// </summary>
    public static class TransactionDirectionBackfill
    {
        public const string Sql = """
            -- Sentinel do materializer (Phase 5a): sem categoria, saída.
            UPDATE financial.transactions
               SET category_id = NULL, direction = 1
             WHERE category_id = '00000000-0000-0000-0000-000000000000';

            -- Direção a partir da categoria, incluindo arquivadas
            -- (categories.kind: Income = 1 → Inflow = 0; Expense = 0 → Outflow = 1).
            UPDATE financial.transactions t
               SET direction = CASE WHEN c.kind = 1 THEN 0 ELSE 1 END
              FROM financial.categories c
             WHERE c.id = t.category_id
               AND c.tenant_id = t.tenant_id
               AND t.kind = 0;
            """;
    }
}
