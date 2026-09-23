using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                schema: "financial",
                table: "import_batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "category_id",
                schema: "financial",
                table: "categorization_rules",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<short>(
                name: "action",
                schema: "financial",
                table: "categorization_rules",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "target_account_id",
                schema: "financial",
                table: "categorization_rules",
                type: "uuid",
                nullable: true);

            // Phase 6.5 grupo 7 — categoria XOR conta alvo, conforme a ação
            // (0 = SetCategory, 1 = MarkAsTransfer). Regras existentes ficam
            // com action = 0 e category_id preenchido, e passam o CHECK.
            migrationBuilder.Sql("""
                ALTER TABLE financial.categorization_rules
                    ADD CONSTRAINT chk_categorization_rules_action CHECK (
                        (action = 0 AND category_id IS NOT NULL AND target_account_id IS NULL)
                        OR (action = 1 AND category_id IS NULL AND target_account_id IS NOT NULL));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Regras de transferência não têm categoria: sem elas, category_id
            // pode voltar a NOT NULL.
            migrationBuilder.Sql("""
                ALTER TABLE financial.categorization_rules DROP CONSTRAINT IF EXISTS chk_categorization_rules_action;
                DELETE FROM financial.categorization_rules WHERE action = 1;
                """);

            migrationBuilder.DropColumn(
                name: "account_id",
                schema: "financial",
                table: "import_batches");

            migrationBuilder.DropColumn(
                name: "action",
                schema: "financial",
                table: "categorization_rules");

            migrationBuilder.DropColumn(
                name: "target_account_id",
                schema: "financial",
                table: "categorization_rules");

            migrationBuilder.AlterColumn<Guid>(
                name: "category_id",
                schema: "financial",
                table: "categorization_rules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
