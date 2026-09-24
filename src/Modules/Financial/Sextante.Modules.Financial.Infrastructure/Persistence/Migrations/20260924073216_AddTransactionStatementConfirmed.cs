using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionStatementConfirmed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "statement_confirmed",
                schema: "financial",
                table: "transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Pernas próprias criadas pelo import (grupo 7) levam a regra que as
            // marcou: vieram do extrato da própria conta, logo já confirmadas.
            // Contrapernas automáticas e transferências manuais ficam false.
            migrationBuilder.Sql("""
                UPDATE financial.transactions
                SET statement_confirmed = true
                WHERE kind = 1 AND categorization_rule_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "statement_confirmed",
                schema: "financial",
                table: "transactions");
        }
    }
}
