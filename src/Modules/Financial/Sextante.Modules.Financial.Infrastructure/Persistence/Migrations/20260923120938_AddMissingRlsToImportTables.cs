using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 6.5 §0.2 — a migration AddCsvImport (Phase 4) criou
    /// categorization_rules, import_profiles e import_batches sem RLS.
    /// Mesmo padrão de AddBudgetsAndAlerts: ENABLE + FORCE, policy de
    /// isolamento por tenant, CHECK contra o sentinel do pool e grants
    /// para sextante_app. Só SQL — o modelo EF não muda.
    /// </summary>
    public partial class AddMissingRlsToImportTables : Migration
    {
        private static readonly string[] Tables =
            ["categorization_rules", "import_profiles", "import_batches"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE financial.{table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE financial.{table} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY {table}_tenant_isolation ON financial.{table}
                        USING (tenant_id = current_setting('app.current_tenant_id', false)::uuid)
                        WITH CHECK (tenant_id = current_setting('app.current_tenant_id', false)::uuid);
                    ALTER TABLE financial.{table}
                        ADD CONSTRAINT {table}_tenant_id_not_sentinel
                        CHECK (tenant_id <> 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid);
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
                            GRANT SELECT, INSERT, UPDATE, DELETE ON financial.{table} TO sextante_app;
                        END IF;
                    END
                    $$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS {table}_tenant_isolation ON financial.{table};
                    ALTER TABLE financial.{table} DISABLE ROW LEVEL SECURITY;
                    ALTER TABLE financial.{table} DROP CONSTRAINT IF EXISTS {table}_tenant_id_not_sentinel;
                    """);
            }
        }
    }
}
