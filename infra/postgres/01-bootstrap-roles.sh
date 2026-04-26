#!/bin/sh
# Bootstrap dos roles do módulo Identity. Corre como o Postgres entrypoint
# na PRIMEIRA boot do container (ficheiros em /docker-entrypoint-initdb.d/
# correm como o superuser definido por POSTGRES_USER).
#
# As passwords vêm de SEXTANTE_MIGRATIONS_PASSWORD e SEXTANTE_APP_PASSWORD
# (passadas pelo docker-compose ao serviço postgres). Se faltarem, falha o
# init — preferimos falhar a boot do que ficar com defaults conhecidos
# publicamente.
#
# Rotação de password posterior à primeira boot: ALTER ROLE manual via
# psql — Postgres init scripts não voltam a correr depois do volume estar
# inicializado.

set -e

: "${SEXTANTE_MIGRATIONS_PASSWORD:?SEXTANTE_MIGRATIONS_PASSWORD env var is required for role bootstrap}"
: "${SEXTANTE_APP_PASSWORD:?SEXTANTE_APP_PASSWORD env var is required for role bootstrap}"
: "${POSTGRES_DB:?POSTGRES_DB env var is required}"

psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    --set ON_ERROR_STOP=on \
    --set migrations_password="$SEXTANTE_MIGRATIONS_PASSWORD" \
    --set app_password="$SEXTANTE_APP_PASSWORD" \
    --set db_name="$POSTGRES_DB" <<'EOSQL'
DO $bootstrap$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_migrations') THEN
        EXECUTE format('CREATE ROLE sextante_migrations BYPASSRLS LOGIN PASSWORD %L', :'migrations_password');
    ELSE
        EXECUTE format('ALTER ROLE sextante_migrations WITH LOGIN PASSWORD %L', :'migrations_password');
    END IF;

    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN
        EXECUTE format('CREATE ROLE sextante_app NOBYPASSRLS LOGIN PASSWORD %L', :'app_password');
    ELSE
        EXECUTE format('ALTER ROLE sextante_app WITH LOGIN PASSWORD %L', :'app_password');
    END IF;
END
$bootstrap$;

-- Ownership da BD para sextante_migrations: simplifica DDL (CREATE SCHEMA,
-- CREATE TABLE) e evita corner cases do Postgres 15+ com permissões em
-- public schema.
ALTER DATABASE :"db_name" OWNER TO sextante_migrations;

GRANT CONNECT ON DATABASE :"db_name" TO sextante_app;
GRANT USAGE ON SCHEMA public TO sextante_app;
EOSQL
