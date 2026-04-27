using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPrimaryCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrimaryCurrency",
                schema: "shared",
                table: "Tenants",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EUR");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrimaryCurrency",
                schema: "shared",
                table: "Tenants");
        }
    }
}
