using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sextante.Modules.Financial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableFinancialRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Garantir que sextante_app tem CRUD nas tabelas do schema
            // financial. Sem privilégios DDL — esses ficam para o role
            // sextante_migrations que corre esta migration.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
                        GRANT USAGE ON SCHEMA financial TO sextante_app;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA financial TO sextante_app;
                        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA financial TO sextante_app;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA financial
                            GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO sextante_app;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA financial
                            GRANT USAGE, SELECT ON SEQUENCES TO sextante_app;
                    END IF;
                END
                $$;
                """);

            // RLS + FORCE em accounts/categories/transactions, com policy
            // por tenant_id baseada em current_setting('app.current_tenant_id').
            // O TenantConnectionInterceptor (Identity.Infrastructure) já garante
            // que o GUC está populado em cada checkout do pool — Phase 2 reusa
            // exatamente o mesmo mecanismo.
            migrationBuilder.Sql("""
                ALTER TABLE financial.accounts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.accounts FORCE ROW LEVEL SECURITY;
                CREATE POLICY accounts_tenant_isolation ON financial.accounts
                    USING (tenant_id = current_setting('app.current_tenant_id', false)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant_id', false)::uuid);

                ALTER TABLE financial.categories ENABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.categories FORCE ROW LEVEL SECURITY;
                CREATE POLICY categories_tenant_isolation ON financial.categories
                    USING (tenant_id = current_setting('app.current_tenant_id', false)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant_id', false)::uuid);

                ALTER TABLE financial.transactions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE financial.transactions FORCE ROW LEVEL SECURITY;
                CREATE POLICY transactions_tenant_isolation ON financial.transactions
                    USING (tenant_id = current_setting('app.current_tenant_id', false)::uuid)
                    WITH CHECK (tenant_id = current_setting('app.current_tenant_id', false)::uuid);
                """);

            // Tags default '[]' — EF gera coluna jsonb sem default. Defesa
            // contra leitura de uma transação criada fora do pipeline.
            migrationBuilder.Sql("""
                ALTER TABLE financial.transactions
                    ALTER COLUMN tags SET DEFAULT '[]'::jsonb;
                """);

            // Defesa em profundidade: tenant_id nunca pode ser o sentinel
            // anonymous (ffffffff-...). Mesmo padrão usado em shared.* na
            // migration HardenTenantSentinelGuard.
            migrationBuilder.Sql("""
                ALTER TABLE financial.accounts
                    ADD CONSTRAINT accounts_tenant_id_not_sentinel
                    CHECK (tenant_id <> 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid);
                ALTER TABLE financial.categories
                    ADD CONSTRAINT categories_tenant_id_not_sentinel
                    CHECK (tenant_id <> 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid);
                ALTER TABLE financial.transactions
                    ADD CONSTRAINT transactions_tenant_id_not_sentinel
                    CHECK (tenant_id <> 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE financial.transactions DROP CONSTRAINT IF EXISTS transactions_tenant_id_not_sentinel;
                ALTER TABLE financial.categories DROP CONSTRAINT IF EXISTS categories_tenant_id_not_sentinel;
                ALTER TABLE financial.accounts DROP CONSTRAINT IF EXISTS accounts_tenant_id_not_sentinel;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE financial.transactions ALTER COLUMN tags DROP DEFAULT;
                """);
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS transactions_tenant_isolation ON financial.transactions;
                ALTER TABLE financial.transactions DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS categories_tenant_isolation ON financial.categories;
                ALTER TABLE financial.categories DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS accounts_tenant_isolation ON financial.accounts;
                ALTER TABLE financial.accounts DISABLE ROW LEVEL SECURITY;
                """);
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
                        REVOKE SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA financial FROM sextante_app;
                        REVOKE USAGE, SELECT ON ALL SEQUENCES IN SCHEMA financial FROM sextante_app;
                        REVOKE USAGE ON SCHEMA financial FROM sextante_app;
                    END IF;
                END
                $$;
                """);
        }
    }
}
