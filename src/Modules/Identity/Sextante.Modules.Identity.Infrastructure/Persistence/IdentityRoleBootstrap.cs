namespace Sextante.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// SQL idempotente que cria os roles <c>sextante_migrations</c> e
/// <c>sextante_app</c> usados pelo módulo Identity. Devolvido como
/// string para que o test fixture e o init script do compose
/// reutilizem a mesma definição.
/// </summary>
public static class IdentityRoleBootstrap
{
    public static string BuildSql(string migrationsPassword, string appPassword)
    {
        // Single-quote escaping: passwords são user-controlled e podem conter '.
        var migEscaped = migrationsPassword.Replace("'", "''");
        var appEscaped = appPassword.Replace("'", "''");

        return
            "DO $bootstrap$\n" +
            "BEGIN\n" +
            "    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_migrations') THEN\n" +
            "        CREATE ROLE sextante_migrations BYPASSRLS LOGIN PASSWORD '" + migEscaped + "';\n" +
            "    ELSE\n" +
            "        ALTER ROLE sextante_migrations WITH LOGIN PASSWORD '" + migEscaped + "';\n" +
            "    END IF;\n" +
            "    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'sextante_app') THEN\n" +
            "        CREATE ROLE sextante_app NOBYPASSRLS LOGIN PASSWORD '" + appEscaped + "';\n" +
            "    ELSE\n" +
            "        ALTER ROLE sextante_app WITH LOGIN PASSWORD '" + appEscaped + "';\n" +
            "    END IF;\n" +
            "END\n" +
            "$bootstrap$;";
    }

    /// <summary>
    /// Permissões de DDL ao role migrations (criar schema/tabelas).
    /// Tem de ser executado por um superuser depois dos roles existirem.
    /// </summary>
    public static string BuildGrantSql(string databaseName)
    {
        // ALTER DATABASE owner faz com que sextante_migrations herde implicitamente
        // CONNECT/CREATE/TEMP — evita corner cases do Postgres 15+ onde GRANT
        // ALL às vezes ainda exige checks adicionais.
        return
            $"ALTER DATABASE \"{databaseName}\" OWNER TO sextante_migrations;\n" +
            $"GRANT ALL PRIVILEGES ON DATABASE \"{databaseName}\" TO sextante_migrations;\n" +
            $"GRANT CONNECT ON DATABASE \"{databaseName}\" TO sextante_app;\n" +
            "GRANT USAGE, CREATE ON SCHEMA public TO sextante_migrations;\n" +
            "GRANT USAGE ON SCHEMA public TO sextante_app;";
    }
}
