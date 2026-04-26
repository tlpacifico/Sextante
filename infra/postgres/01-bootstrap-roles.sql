-- Bootstrap de roles do módulo Identity. Executado pelo Postgres
-- entrypoint na primeira boot do container (ficheiros em
-- /docker-entrypoint-initdb.d/ são executados como o superuser).
--
-- As passwords reais devem ser sobrepostas via env vars antes da
-- primeira ligação dos serviços (variáveis SEXTANTE_MIGRATIONS_PASSWORD e
-- SEXTANTE_APP_PASSWORD não devem entrar aqui — este ficheiro só serve
-- para garantir que os roles existem; a aplicação trata de ALTER ROLE
-- via env-var-driven bootstrap sempre que arranca, idempotente).

DO $bootstrap$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_migrations') THEN
        CREATE ROLE sextante_migrations BYPASSRLS LOGIN PASSWORD 'changeme_migrations';
    END IF;

    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
        CREATE ROLE sextante_app NOBYPASSRLS LOGIN PASSWORD 'changeme_app';
    END IF;
END
$bootstrap$;

-- Ownership da BD para sextante_migrations: simplifica DDL (CREATE SCHEMA,
-- CREATE TABLE) e evita corner cases do Postgres 15+ com permissões em
-- public schema.
ALTER DATABASE sextante OWNER TO sextante_migrations;

GRANT CONNECT ON DATABASE sextante TO sextante_app;
GRANT USAGE ON SCHEMA public TO sextante_app;
