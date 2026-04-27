using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinancial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "financial");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    opening_balance_amount = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    opening_balance_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    icon_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    color_hex = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "financial",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    tags = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_tenant_id",
                schema: "financial",
                table: "accounts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_tenant_id_deleted_at",
                schema: "financial",
                table: "accounts",
                columns: new[] { "tenant_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_categories_tenant_id",
                schema: "financial",
                table: "categories",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_categories_tenant_id_kind_deleted_at",
                schema: "financial",
                table: "categories",
                columns: new[] { "tenant_id", "kind", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_tenant_id",
                schema: "financial",
                table: "transactions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_tenant_id_account_id_deleted_at",
                schema: "financial",
                table: "transactions",
                columns: new[] { "tenant_id", "account_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_tenant_id_category_id_deleted_at",
                schema: "financial",
                table: "transactions",
                columns: new[] { "tenant_id", "category_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_tenant_id_occurred_at",
                schema: "financial",
                table: "transactions",
                columns: new[] { "tenant_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "financial");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "financial");
        }
    }
}
