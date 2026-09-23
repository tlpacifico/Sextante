using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 6.5 §2.2 — data do saldo inicial (<c>opening_balance_date</c>),
    /// imutável após criação da conta, e CHECK que restringe saldo inicial
    /// negativo a contas do tipo CreditCard (requirements.md D6).
    /// </summary>
    public partial class AddAccountOpeningBalanceDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default temporário = hoje: as linhas existentes ficam
            // coerentes mesmo antes do backfill; o default sai no fim.
            migrationBuilder.AddColumn<DateOnly>(
                name: "opening_balance_date",
                schema: "financial",
                table: "accounts",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE");

            migrationBuilder.Sql(AccountOpeningBalanceDateBackfill.Sql);

            migrationBuilder.Sql("""
                ALTER TABLE financial.accounts ALTER COLUMN opening_balance_date DROP DEFAULT;
                ALTER TABLE financial.accounts
                    ADD CONSTRAINT chk_accounts_opening_balance_non_negative_unless_credit_card
                    CHECK (opening_balance_amount >= 0 OR type = 3);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE financial.accounts DROP CONSTRAINT IF EXISTS chk_accounts_opening_balance_non_negative_unless_credit_card;
                """);

            migrationBuilder.DropColumn(
                name: "opening_balance_date",
                schema: "financial",
                table: "accounts");
        }
    }

    /// <summary>
    /// Backfill da data do saldo inicial. Idempotente — corre na migration e
    /// nos testes.
    /// </summary>
    public static class AccountOpeningBalanceDateBackfill
    {
        public const string Sql = """
            -- Phase 6.5 §2.2 — contas existentes não tinham data do saldo inicial;
            -- usa a data de criação da conta (requirements.md D6).
            UPDATE financial.accounts
               SET opening_balance_date = created_at::date;
            """;
    }
}
