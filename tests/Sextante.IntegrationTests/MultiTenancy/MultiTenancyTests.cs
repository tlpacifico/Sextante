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
    public async Task CrossTenant_write_blocked_by_RLS_at_DB_layer()
    {
        var client = _fixture.Factory.CreateClient();
        var (userA, tenantA) = await Signup(client, "alpha");
        var (_, tenantB) = await Signup(client, "beta");

        // Conectar como sextante_app (NOBYPASSRLS), simular tenant A,
        // tentar UPDATE numa membership cujo tenant_id é B.
        await using var conn = _fixture.OpenAppConnection();
        await ExecuteAsync(conn, "SELECT set_config('app.current_tenant_id', @tid, false)",
            ("tid", tenantA.ToString()));

        // O UPDATE não devolve 0 rows — a RLS policy filtra antes; mas se tentarmos
        // forçar com um WHERE explícito incluindo o tenant_id de B, a row é invisível.
        await using var cmd = new NpgsqlCommand(
            "UPDATE shared.\"Memberships\" SET \"Role\" = 'ReadOnly' WHERE tenant_id = @b",
            conn);
        cmd.Parameters.AddWithValue("b", tenantB);
        var affected = await cmd.ExecuteNonQueryAsync();

        affected.Should().Be(0, "RLS USING clause torna a row de B invisível para tenant A.");
    }

    [Fact]
    public async Task Insert_as_app_role_without_tenant_set_is_rejected_by_RLS()
    {
        var client = _fixture.Factory.CreateClient();
        await Signup(client, "gamma");

        await using var conn = _fixture.OpenAppConnection();

        // Sem set_config, a policy WITH CHECK (USING) lança porque
        // current_setting('app.current_tenant_id', false) falha.
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO shared.\"Memberships\"(\"Id\",\"UserId\",tenant_id,\"Role\",\"CreatedAt\",\"UpdatedAt\",\"Version\") " +
            "VALUES (@id, @u, @t, 'Owner', now(), now(), 1)",
            conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("u", Guid.NewGuid());
        cmd.Parameters.AddWithValue("t", Guid.NewGuid());

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var ex = await act.Should().ThrowAsync<PostgresException>();
        // SQLSTATE pode variar: 42501 (insufficient_privilege) quando RLS rejeita,
        // 22P02 (invalid_text_representation) quando current_setting devolve ''
        // e o cast ::uuid falha, ou 42704 quando o setting não existe.
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

    private async Task<(Guid UserId, Guid TenantId)> Signup(HttpClient client, string label)
    {
        var email = $"{label}-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password = "Password123!",
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
