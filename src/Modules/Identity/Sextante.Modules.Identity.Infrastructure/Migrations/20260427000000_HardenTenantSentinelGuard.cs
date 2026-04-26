using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// Defesa em profundidade contra colisão entre <c>TenantId</c> real e o
    /// sentinel anonymous. Antes, ambos podiam ser <c>Guid.Empty</c>, abrindo
    /// a porta a um anonymous request inserir uma row tenant-owned com
    /// <c>tenant_id = Guid.Empty</c> e RLS aceitar (sentinel matches sentinel).
    /// Agora o sentinel é <c>ffffffff-ffff-ffff-ffff-ffffffffffff</c> e o
    /// CHECK constraint proíbe esse valor (e <c>Guid.Empty</c> por hygiene)
    /// como <c>TenantId</c> real.
    /// </summary>
    public partial class HardenTenantSentinelGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE shared."Tenants"
                    ADD CONSTRAINT "CK_Tenants_Id_NotSentinel"
                    CHECK ("Id" NOT IN (
                        '00000000-0000-0000-0000-000000000000'::uuid,
                        'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid
                    ));

                ALTER TABLE shared."Memberships"
                    ADD CONSTRAINT "CK_Memberships_TenantId_NotSentinel"
                    CHECK (tenant_id NOT IN (
                        '00000000-0000-0000-0000-000000000000'::uuid,
                        'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid
                    ));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE shared."Memberships"
                    DROP CONSTRAINT IF EXISTS "CK_Memberships_TenantId_NotSentinel";

                ALTER TABLE shared."Tenants"
                    DROP CONSTRAINT IF EXISTS "CK_Tenants_Id_NotSentinel";
                """);
        }
    }
}
