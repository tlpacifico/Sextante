using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCsvImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "categorization_rule_id",
                schema: "financial",
                table: "transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "categorized_at",
                schema: "financial",
                table: "transactions",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "categorization_rules",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    pattern = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    match_type = table.Column<string>(type: "text", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categorization_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    imported_rows = table.Column<int>(type: "integer", nullable: false),
                    duplicate_rows = table.Column<int>(type: "integer", nullable: false),
                    error_rows = table.Column<int>(type: "integer", nullable: false),
                    parsed_preview = table.Column<string>(type: "jsonb", nullable: true),
                    preview_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    categorization_result = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_profiles",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    delimiter = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    has_header_row = table.Column<bool>(type: "boolean", nullable: false),
                    date_format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    decimal_separator = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    skip_rows = table.Column<int>(type: "integer", nullable: false),
                    column_mappings = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_categorization_rule_id",
                schema: "financial",
                table: "transactions",
                column: "categorization_rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_categorization_rules_tenant_id_priority",
                schema: "financial",
                table: "categorization_rules",
                columns: new[] { "tenant_id", "priority" });

            migrationBuilder.AddForeignKey(
                name: "FK_transactions_categorization_rules_categorization_rule_id",
                schema: "financial",
                table: "transactions",
                column: "categorization_rule_id",
                principalSchema: "financial",
                principalTable: "categorization_rules",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_transactions_categorization_rules_categorization_rule_id",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "categorization_rules",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "import_batches",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "import_profiles",
                schema: "financial");

            migrationBuilder.DropIndex(
                name: "IX_transactions_categorization_rule_id",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "categorization_rule_id",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "categorized_at",
                schema: "financial",
                table: "transactions");
        }
    }
}
