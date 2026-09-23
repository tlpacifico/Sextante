using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 6.5 §5.2 — definições do cartão de crédito em
    /// <c>financial.accounts</c> (limite, dia de fecho, dia de pagamento, conta
    /// de pagamento), todas nullable. CHECKs: só em contas CreditCard e
    /// preenchidas todas ou nenhuma (a conta de pagamento é opcional); limite
    /// positivo na moeda da conta; dias 1–31. A conta de pagamento é uma soft
    /// reference, sem FK. O RLS de <c>accounts</c> já cobre as colunas novas.
    /// </summary>
    public partial class AddCreditCardSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "credit_card_limit_amount",
                schema: "financial",
                table: "accounts",
                type: "numeric(20,8)",
                precision: 20,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "credit_card_limit_currency",
                schema: "financial",
                table: "accounts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "credit_card_payment_account_id",
                schema: "financial",
                table: "accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "credit_card_payment_due_day",
                schema: "financial",
                table: "accounts",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "credit_card_statement_closing_day",
                schema: "financial",
                table: "accounts",
                type: "smallint",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE financial.accounts
                    ADD CONSTRAINT chk_accounts_credit_card_settings_all_or_none
                    CHECK (
                        (credit_card_limit_amount IS NULL AND credit_card_limit_currency IS NULL
                         AND credit_card_statement_closing_day IS NULL AND credit_card_payment_due_day IS NULL
                         AND credit_card_payment_account_id IS NULL)
                        OR
                        (type = 3 AND credit_card_limit_amount IS NOT NULL AND credit_card_limit_currency IS NOT NULL
                         AND credit_card_statement_closing_day IS NOT NULL AND credit_card_payment_due_day IS NOT NULL)
                    ),
                    ADD CONSTRAINT chk_accounts_credit_card_limit_positive
                    CHECK (credit_card_limit_amount IS NULL OR credit_card_limit_amount > 0),
                    ADD CONSTRAINT chk_accounts_credit_card_limit_currency
                    CHECK (credit_card_limit_currency IS NULL OR credit_card_limit_currency = currency),
                    ADD CONSTRAINT chk_accounts_credit_card_days
                    CHECK ((credit_card_statement_closing_day IS NULL OR credit_card_statement_closing_day BETWEEN 1 AND 31)
                       AND (credit_card_payment_due_day IS NULL OR credit_card_payment_due_day BETWEEN 1 AND 31));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE financial.accounts DROP CONSTRAINT IF EXISTS chk_accounts_credit_card_settings_all_or_none;
                ALTER TABLE financial.accounts DROP CONSTRAINT IF EXISTS chk_accounts_credit_card_limit_positive;
                ALTER TABLE financial.accounts DROP CONSTRAINT IF EXISTS chk_accounts_credit_card_limit_currency;
                ALTER TABLE financial.accounts DROP CONSTRAINT IF EXISTS chk_accounts_credit_card_days;
                """);

            migrationBuilder.DropColumn(
                name: "credit_card_limit_amount",
                schema: "financial",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "credit_card_limit_currency",
                schema: "financial",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "credit_card_payment_account_id",
                schema: "financial",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "credit_card_payment_due_day",
                schema: "financial",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "credit_card_statement_closing_day",
                schema: "financial",
                table: "accounts");
        }
    }
}
