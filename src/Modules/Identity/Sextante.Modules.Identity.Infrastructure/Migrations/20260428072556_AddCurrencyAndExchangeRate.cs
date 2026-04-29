using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Infrastructure.Migrations.Data;

#nullable disable

namespace Sextante.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyAndExchangeRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "currencies",
                schema: "shared",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    minor_units = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currencies", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "ecb_snapshot_state",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ecb_snapshot_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rates",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    from_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    to_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rates", x => x.id);
                    table.ForeignKey(
                        name: "FK_exchange_rates_currencies_to_currency",
                        column: x => x.to_currency,
                        principalSchema: "shared",
                        principalTable: "currencies",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_currencies_is_active",
                schema: "shared",
                table: "currencies",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rates_rate_date",
                schema: "shared",
                table: "exchange_rates",
                column: "rate_date");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rates_rate_date_from_currency_to_currency",
                schema: "shared",
                table: "exchange_rates",
                columns: new[] { "rate_date", "from_currency", "to_currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rates_to_currency",
                schema: "shared",
                table: "exchange_rates",
                column: "to_currency");

            // Reference data partilhada entre tenants — sem RLS. Aplicação
            // pode SELECT/INSERT/UPDATE; o role app é GRANTed em
            // schema-level no init de Postgres (infra/postgres).
            migrationBuilder.Sql(
                "ALTER TABLE \"shared\".\"currencies\" DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "ALTER TABLE \"shared\".\"exchange_rates\" DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "ALTER TABLE \"shared\".\"ecb_snapshot_state\" DISABLE ROW LEVEL SECURITY;");

            // Seed ISO 4217 fiat. Crypto out (Phase 8). Timestamps fixos
            // (não DateTimeOffset.UtcNow) para que a migration seja
            // determinística (re-running em dev produz o mesmo state).
            var seedAt = new DateTimeOffset(2026, 4, 28, 0, 0, 0, TimeSpan.Zero);
            foreach (var entry in Iso4217Currencies.All)
            {
                migrationBuilder.InsertData(
                    schema: "shared",
                    table: "currencies",
                    columns: new[] { "code", "name", "symbol", "minor_units", "is_active", "created_at", "updated_at", "version" },
                    values: new object[] { entry.Code, entry.Name, entry.Symbol, entry.MinorUnits, true, seedAt, seedAt, 1 });
            }

            // Single-row state — id sempre 1 (SingletonId).
            migrationBuilder.InsertData(
                schema: "shared",
                table: "ecb_snapshot_state",
                columns: new[] { "id", "last_run_at", "last_success_at", "last_error" },
                values: new object[] { EcbSnapshotState.SingletonId, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ecb_snapshot_state",
                schema: "shared");

            migrationBuilder.DropTable(
                name: "exchange_rates",
                schema: "shared");

            migrationBuilder.DropTable(
                name: "currencies",
                schema: "shared");
        }
    }
}
