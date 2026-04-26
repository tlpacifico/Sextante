using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnableRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Roles `sextante_migrations` (BYPASSRLS) e `sextante_app` (NOBYPASSRLS)
            // são criadas externamente — pelo init script do compose em produção
            // (infra/postgres/01-bootstrap-roles.sh) e pelo IdentityIntegrationFixture
            // nos testes. Manter a criação aqui geraria circularidade: migrations
            // correm como sextante_migrations e este role tem de existir antes da
            // primeira migration.
            //
            // Esta migration NÃO é idempotente fora do controlo do EF: re-execução
            // manual falha com `policy already exists`. EF protege via
            // __migrations history table — mas não invocar via SQL solto.

            // 1. Permissões — sextante_app só faz CRUD; nada de DDL.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
                        GRANT USAGE ON SCHEMA shared TO sextante_app;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA shared TO sextante_app;
                        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA shared TO sextante_app;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA shared
                            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO sextante_app;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA shared
                            GRANT USAGE, SELECT ON SEQUENCES TO sextante_app;
                    END IF;
                END
                $$;
                """);

            // 2. FK soft entre Memberships.tenant_id e Tenants.Id (não declarada
            //    no modelo porque o tipo do FK é o value object TenantId).
            migrationBuilder.Sql("""
                ALTER TABLE shared."Memberships"
                    ADD CONSTRAINT "FK_Memberships_Tenants_TenantId"
                    FOREIGN KEY (tenant_id)
                    REFERENCES shared."Tenants"("Id")
                    ON DELETE CASCADE;
                """);

            // 3. RLS nas tabelas tenant-owned. FORCE garante que mesmo o owner
            //    da tabela passa pela policy (defesa em profundidade).
            //    A coluna que identifica o tenant difere entre Memberships
            //    (FK tenant_id) e Tenants (a própria PK Id).
            migrationBuilder.Sql("""
                ALTER TABLE shared."Memberships" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE shared."Memberships" FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON shared."Memberships"
                    USING (tenant_id = current_setting('app.current_tenant_id', false)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant_id', false)::uuid);

                ALTER TABLE shared."Tenants" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE shared."Tenants" FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_self_isolation ON shared."Tenants"
                    USING ("Id" = current_setting('app.current_tenant_id', false)::uuid)
                    WITH CHECK ("Id" = current_setting('app.current_tenant_id', false)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_self_isolation ON shared."Tenants";""");
            migrationBuilder.Sql("""ALTER TABLE shared."Tenants" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DROP POLICY IF EXISTS tenant_isolation ON shared."Memberships";""");
            migrationBuilder.Sql("""ALTER TABLE shared."Memberships" DISABLE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""ALTER TABLE shared."Memberships" DROP CONSTRAINT IF EXISTS "FK_Memberships_Tenants_TenantId";""");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA shared FROM sextante_app;
                        REVOKE USAGE, SELECT ON ALL SEQUENCES IN SCHEMA shared FROM sextante_app;
                        REVOKE USAGE ON SCHEMA shared FROM sextante_app;
                    END IF;
                END
                $$;
                """);
        }
    }
}
