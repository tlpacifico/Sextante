using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 6.5 grupo 6 (revisão profunda) — data da compra no plano de
    /// prestações: só as prestações de compras já na dívida de um fecho se
    /// descontam no próximo pagamento do cartão. Planos existentes: backfill
    /// com a data da compra ligada (UTC) ou, sem compra, um mês antes da
    /// 1.ª prestação.
    /// </summary>
    public partial class AddInstallmentPlanPurchaseDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "purchase_date",
                schema: "financial",
                table: "installment_plans",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.Sql("""
                UPDATE financial.installment_plans p
                   SET purchase_date = COALESCE(
                       (SELECT (t.occurred_at AT TIME ZONE 'UTC')::date
                          FROM financial.transactions t
                         WHERE t.id = p.purchase_transaction_id),
                       (p.first_installment_date - INTERVAL '1 month')::date);
                ALTER TABLE financial.installment_plans ALTER COLUMN purchase_date DROP DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "purchase_date",
                schema: "financial",
                table: "installment_plans");
        }
    }
}
