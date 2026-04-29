using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCurrencyAndExchangeRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Transaction columns: NULL allowed; pré-Phase-3 rows ficam
            // NULL e read-side trata-as como `1.0` via COALESCE.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "exchange_rate_at",
                schema: "financial",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "exchange_rate_to_primary",
                schema: "financial",
                table: "transactions",
                type: "numeric(20,8)",
                precision: 20,
                scale: 8,
                nullable: true);

            // Account.Currency: NOT NULL com default vazio para criar a
            // coluna; default-fill abaixo via UPDATE; depois drop default
            // para forçar fornecimento explícito em inserts futuros.
            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "financial",
                table: "accounts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE ""financial"".""accounts"" a
                SET ""currency"" = COALESCE(t.""PrimaryCurrency"", 'EUR')
                FROM ""shared"".""Tenants"" t
                WHERE t.""Id"" = a.""tenant_id"";
            ");

            migrationBuilder.Sql(
                "ALTER TABLE \"financial\".\"accounts\" ALTER COLUMN \"currency\" DROP DEFAULT;");

            migrationBuilder.Sql(@"
                ALTER TABLE ""financial"".""accounts""
                ADD CONSTRAINT ""FK_accounts_shared_currencies""
                FOREIGN KEY (""currency"")
                REFERENCES ""shared"".""currencies"" (""code"")
                ON DELETE RESTRICT;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"financial\".\"accounts\" DROP CONSTRAINT IF EXISTS \"FK_accounts_shared_currencies\";");

            migrationBuilder.DropColumn(
                name: "exchange_rate_at",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "exchange_rate_to_primary",
                schema: "financial",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "financial",
                table: "accounts");
        }
    }
}
