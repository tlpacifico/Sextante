using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.MultiTenancy;

/// <summary>
/// Suite §4.7 do tech-stack — invariante de privacidade tenant→tenant.
/// </summary>
public sealed class MultiTenancyTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public MultiTenancyTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CrossTenant_read_isolated_via_RLS()
    {
        var client = _fixture.Factory.CreateClient();
        var (_, tenantA) = await Signup(client, "alpha-read");
        var (_, tenantB) = await Signup(client, "beta-read");

        await using var conn = _fixture.OpenAppConnection();

        // Como sextante_app + GUC=tenantA: SELECT só vê membership de A.
        await ExecuteAsync(conn,
            "SELECT set_config('app.current_tenant_id', @tid, false)",
            ("tid", tenantA.ToString()));

        await using (var read = conn.CreateCommand())
        {
            read.CommandText =
                "SELECT count(*) FROM shared.\"Memberships\" WHERE tenant_id = @b";
            read.Parameters.AddWithValue("b", tenantB);
            var count = (long)(await read.ExecuteScalarAsync())!;
            count.Should().Be(0,
                "RLS USING clause torna a row de B invisível mesmo com WHERE explícito.");
        }

        // Sanity: a row de B existe (vista como superuser).
        await using var super = _fixture.OpenSuperuserConnection();
        await using (var read = super.CreateCommand())
        {
            read.CommandText =
                "SELECT count(*) FROM shared.\"Memberships\" WHERE tenant_id = @b";
            read.Parameters.AddWithValue("b", tenantB);
            var count = (long)(await read.ExecuteScalarAsync())!;
            count.Should().Be(1, "superuser não passa pela RLS, vê a row de B.");
        }
    }

    [Fact]
    public async Task CrossTenant_write_throws_42501_at_DB_layer()
    {
        var client = _fixture.Factory.CreateClient();
        var (_, tenantA) = await Signup(client, "alpha-write");
        var (_, tenantB) = await Signup(client, "beta-write");

        // Conectar como sextante_app, simular tenant A; tentar INSERT
        // de uma membership cujo tenant_id é B → WITH CHECK rejeita
        // com SqlState 42501 (insufficient_privilege para a policy).
        await using var conn = _fixture.OpenAppConnection();
        await ExecuteAsync(conn,
            "SELECT set_config('app.current_tenant_id', @tid, false)",
            ("tid", tenantA.ToString()));

        // Precisamos de um UserId que exista (FK Memberships.UserId →
        // AspNetUsers.Id) — usar o owner do tenant B, lido via superuser.
        Guid victimUserId;
        await using (var super = _fixture.OpenSuperuserConnection())
        {
            await using var lookup = super.CreateCommand();
            lookup.CommandText =
                "SELECT \"UserId\" FROM shared.\"Memberships\" WHERE tenant_id = @b LIMIT 1";
            lookup.Parameters.AddWithValue("b", tenantB);
            victimUserId = (Guid)(await lookup.ExecuteScalarAsync())!;
        }

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO shared.\"Memberships\"" +
            "(\"Id\",\"UserId\",tenant_id,\"Role\",\"CreatedAt\",\"UpdatedAt\",\"Version\")" +
            " VALUES (@id, @u, @t, 'Owner', now(), now(), 1)",
            conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("u", victimUserId);
        cmd.Parameters.AddWithValue("t", tenantB);

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be(
            "42501",
            "RLS WITH CHECK rejeita o INSERT — mensagem do Postgres contém 'row-level security policy'.");
        ex.Which.Message.Should().Contain("row-level security policy");
    }

    [Fact]
    public async Task Insert_as_app_role_without_tenant_set_is_rejected()
    {
        var client = _fixture.Factory.CreateClient();
        await Signup(client, "gamma-no-tenant");

        await using var conn = _fixture.OpenAppConnection();

        // Sem set_config, current_setting('app.current_tenant_id', false)
        // lança 42704 (undefined_object) ou retorna '' que falha o cast
        // ::uuid (22P02). Em ambos os casos é fail-loud.
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO shared.\"Memberships\"" +
            "(\"Id\",\"UserId\",tenant_id,\"Role\",\"CreatedAt\",\"UpdatedAt\",\"Version\")" +
            " VALUES (@id, @u, @t, 'Owner', now(), now(), 1)",
            conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("u", Guid.NewGuid());
        cmd.Parameters.AddWithValue("t", Guid.NewGuid());

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().BeOneOf("42501", "22P02", "42704");
    }

    [Fact]
    public async Task Migrations_run_as_privileged_role_app_role_cannot_assume()
    {
        // Verifica a separação estrutural: sextante_app não tem o role
        // sextante_migrations e portanto não pode assumir BYPASSRLS.
        await using var conn = _fixture.OpenAppConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_has_role('sextante_app', 'sextante_migrations', 'USAGE')",
            conn);
        var result = (bool)(await cmd.ExecuteScalarAsync())!;
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Sentinel_tenant_id_is_rejected_by_CHECK_constraint()
    {
        // Defesa em profundidade: mesmo um superuser não pode inserir
        // tenant_id sentinel — a CHECK constraint da migration
        // HardenTenantSentinelGuard fecha o gap de colisão entre
        // anonymous-sentinel e tenant real.
        await using var conn = _fixture.OpenSuperuserConnection();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO shared.\"Tenants\"(\"Id\",\"Name\",\"CreatedAt\",\"UpdatedAt\",\"Version\")" +
            " VALUES (@id, 'Sentinel attempt', now(), now(), 1)",
            conn);
        cmd.Parameters.AddWithValue(
            "id",
            new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514"); // check_violation
        ex.Which.Message.Should().Contain("CK_Tenants_Id_NotSentinel");
    }

    private async Task<(Guid UserId, Guid TenantId)> Signup(HttpClient client, string label)
    {
        var email = $"{label}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password = "Password123!extra",
            tenantName = $"Tenant {label}",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SignupBody>();
        return (body!.UserId, body.TenantId);
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed record SignupBody(Guid UserId, Guid TenantId);
}
