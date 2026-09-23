using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using Sextante.IntegrationTests.Financial;

namespace Sextante.IntegrationTests.MultiTenancy;

/// <summary>
/// Phase 6.5 §0.2 — as três tabelas do import (Phase 4) ficaram sem RLS.
/// </summary>
public sealed class ImportTablesRlsTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public ImportTablesRlsTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("categorization_rules")]
    [InlineData("import_profiles")]
    [InlineData("import_batches")]
    public async Task Table_has_rls_enabled_and_forced(string table)
    {
        await using var conn = _fixture.OpenSuperuserConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class c " +
            "JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE n.nspname = 'financial' AND c.relname = @t",
            conn);
        cmd.Parameters.AddWithValue("t", table);

        ((bool)(await cmd.ExecuteScalarAsync())!).Should().BeTrue();
    }

    [Fact]
    public async Task Signup_still_seeds_default_import_profile_visible_only_to_its_tenant()
    {
        var (clientA, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rls-imp-a");
        var (_, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rls-imp-b");

        // O seed corre num subscriber assíncrono — esperar até aparecer.
        List<JsonElement>? profiles = null;
        for (var i = 0; i < 50; i++)
        {
            profiles = await clientA.GetFromJsonAsync<List<JsonElement>>("/api/financial/import-profiles");
            if (profiles!.Count > 0)
            {
                break;
            }

            await Task.Delay(100);
        }

        profiles.Should().ContainSingle("o perfil ActivoBank é criado no signup");

        await using var conn = _fixture.OpenAppConnection();
        await SetTenantAsync(conn, tenantA);

        await using var read = new NpgsqlCommand(
            "SELECT count(*) FROM financial.import_profiles WHERE tenant_id = @b",
            conn);
        read.Parameters.AddWithValue("b", tenantB);

        ((long)(await read.ExecuteScalarAsync())!).Should().Be(0);
    }

    [Fact]
    public async Task Cross_tenant_insert_into_categorization_rules_is_rejected()
    {
        var (_, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rls-rule-a");
        var (_, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rls-rule-b");

        await using var conn = _fixture.OpenAppConnection();
        await SetTenantAsync(conn, tenantA);

        await using var insert = new NpgsqlCommand(
            "INSERT INTO financial.categorization_rules " +
            "(id, tenant_id, name, pattern, match_type, category_id, priority, is_active, created_at, updated_at, version) " +
            "VALUES (@id, @b, 'x', 'x', 'Contains', @c, 999, true, now(), now(), 1)",
            conn);
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        insert.Parameters.AddWithValue("b", tenantB);
        insert.Parameters.AddWithValue("c", Guid.NewGuid());

        var act = async () => await insert.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    private static async Task SetTenantAsync(NpgsqlConnection conn, Guid tenantId)
    {
        await using var set = new NpgsqlCommand(
            "SELECT set_config('app.current_tenant_id', @t, false)",
            conn);
        set.Parameters.AddWithValue("t", tenantId.ToString());
        await set.ExecuteScalarAsync();
    }
}
