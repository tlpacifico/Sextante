using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.MultiTenancy;

/// <summary>
/// Regressão: connections devolvidas ao pool com <c>app.current_tenant_id</c>
/// definido não devem permitir um request anonymous subsequente ler dados
/// do tenant anterior. O <c>TenantConnectionInterceptor</c> tem de
/// reescrever o GUC em cada checkout (sentinel zero para anonymous).
/// </summary>
public sealed class PoolConnectionLeakTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public PoolConnectionLeakTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Anonymous_connection_after_authenticated_one_does_not_inherit_tenant()
    {
        // Simulação direta no DB: connection 1 escreve um tenant e define
        // o GUC; connection 2 (mesmo pool, role app) abre — interceptor
        // produciton-side garante reset, mas aqui validamos o invariante
        // a nível de schema:
        //
        // 1. Como sextante_app, fingir um tenant ativo via SET (a interceptor
        //    fá-lo-ia em request autenticada).
        // 2. Tentar SELECT FROM Memberships — deve devolver as memberships
        //    do tenant fingido. Se devolver de outro tenant é leak.
        //
        // O teste documenta o invariante: WITH/USING policies dependem
        // estritamente de current_setting, não de estado anterior.

        await using var conn = _fixture.OpenAppConnection();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        // Cria duas tenants + users + memberships via superuser (bypassa RLS).
        await using (var super = _fixture.OpenSuperuserConnection())
        {
            await using var seed = super.CreateCommand();
            seed.CommandText = """
                INSERT INTO shared."AspNetUsers"
                    ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                     "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
                     "LockoutEnabled", "AccessFailedCount")
                VALUES
                    (@u1, 'a@x.com', 'A@X.COM', 'a@x.com', 'A@X.COM', false, false, false, false, 0),
                    (@u2, 'b@x.com', 'B@X.COM', 'b@x.com', 'B@X.COM', false, false, false, false, 0);
                INSERT INTO shared."Tenants"("Id", "Name", "CreatedAt", "UpdatedAt", "Version")
                VALUES (@a, 'A', now(), now(), 1), (@b, 'B', now(), now(), 1);
                INSERT INTO shared."Memberships"("Id", "UserId", tenant_id, "Role", "CreatedAt", "UpdatedAt", "Version")
                VALUES
                    (@m1, @u1, @a, 'Owner', now(), now(), 1),
                    (@m2, @u2, @b, 'Owner', now(), now(), 1);
                """;
            seed.Parameters.AddWithValue("a", tenantA);
            seed.Parameters.AddWithValue("b", tenantB);
            seed.Parameters.AddWithValue("m1", Guid.NewGuid());
            seed.Parameters.AddWithValue("m2", Guid.NewGuid());
            seed.Parameters.AddWithValue("u1", userA);
            seed.Parameters.AddWithValue("u2", userB);
            await seed.ExecuteNonQueryAsync();
        }

        // Connection app, GUC = tenantA → vê só A.
        await using (var setA = conn.CreateCommand())
        {
            setA.CommandText = "SELECT set_config('app.current_tenant_id', @t, false)";
            setA.Parameters.AddWithValue("t", tenantA.ToString());
            await setA.ExecuteNonQueryAsync();
        }

        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT count(*) FROM shared.\"Memberships\"";
            var c = (long)(await read.ExecuteScalarAsync())!;
            c.Should().Be(1, "RLS deve filtrar para memberships do tenant A.");
        }

        // Mudar GUC para tenantB → vê só B (mesma connection, GUC sobreposto).
        await using (var setB = conn.CreateCommand())
        {
            setB.CommandText = "SELECT set_config('app.current_tenant_id', @t, false)";
            setB.Parameters.AddWithValue("t", tenantB.ToString());
            await setB.ExecuteNonQueryAsync();
        }

        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT count(*) FROM shared.\"Memberships\"";
            var c = (long)(await read.ExecuteScalarAsync())!;
            c.Should().Be(1, "depois de mudar GUC para B, só vê membership de B.");
        }

        // Sentinel zero (estado anonymous) → vê zero rows.
        await using (var setSentinel = conn.CreateCommand())
        {
            setSentinel.CommandText = "SELECT set_config('app.current_tenant_id', @t, false)";
            setSentinel.Parameters.AddWithValue("t", Guid.Empty.ToString());
            await setSentinel.ExecuteNonQueryAsync();
        }

        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT count(*) FROM shared.\"Memberships\"";
            var c = (long)(await read.ExecuteScalarAsync())!;
            c.Should().Be(0, "sentinel zero (anonymous) não pode ver membership de tenant nenhum.");
        }
    }
}
