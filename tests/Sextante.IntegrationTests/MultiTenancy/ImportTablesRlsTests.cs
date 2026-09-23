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

    [Theory]
    [InlineData("categorization_rules")]
    [InlineData("import_profiles")]
    [InlineData("import_batches")]
    public async Task Cross_tenant_insert_is_rejected(string table)
    {
        var (_, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, $"rls-w-{table}-a");
        var (_, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, $"rls-w-{table}-b");

        await using var conn = _fixture.OpenAppConnection();
        await SetTenantAsync(conn, tenantA);

        var act = async () => await InsertRowAsync(conn, table, tenantB);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Theory]
    [InlineData("categorization_rules")]
    [InlineData("import_profiles")]
    [InlineData("import_batches")]
    public async Task Rows_of_another_tenant_are_invisible(string table)
    {
        var (_, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, $"rls-r-{table}-a");
        var (_, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, $"rls-r-{table}-b");

        await using (var connB = _fixture.OpenAppConnection())
        {
            await SetTenantAsync(connB, tenantB);
            await InsertRowAsync(connB, table, tenantB);
        }

        await using var connA = _fixture.OpenAppConnection();
        await SetTenantAsync(connA, tenantA);
        await using var read = new NpgsqlCommand(
            $"SELECT count(*) FROM financial.{table} WHERE tenant_id = @b",
            connA);
        read.Parameters.AddWithValue("b", tenantB);

        ((long)(await read.ExecuteScalarAsync())!).Should().Be(0);
    }

    [Theory]
    [InlineData("categorization_rules")]
    [InlineData("import_profiles")]
    [InlineData("import_batches")]
    public async Task Sentinel_tenant_id_is_rejected_by_CHECK_constraint(string table)
    {
        // Superuser não passa pela RLS — só a CHECK pode travar o sentinel.
        await using var conn = _fixture.OpenSuperuserConnection();

        var act = async () => await InsertRowAsync(conn, table, new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be($"{table}_tenant_id_not_sentinel");
    }

    private static async Task InsertRowAsync(NpgsqlConnection conn, string table, Guid tenantId)
    {
        var sql = table switch
        {
            "categorization_rules" =>
                "INSERT INTO financial.categorization_rules " +
                "(id, tenant_id, name, pattern, match_type, category_id, priority, is_active, created_at, updated_at, version) " +
                "VALUES (@id, @t, 'x', 'x', 'Contains', @id, 999, true, now(), now(), 1)",
            "import_profiles" =>
                "INSERT INTO financial.import_profiles " +
                "(id, tenant_id, name, delimiter, has_header_row, date_format, decimal_separator, skip_rows, column_mappings, created_at, updated_at, version) " +
                "VALUES (@id, @t, 'x', ';', true, 'dd/MM/yyyy', ',', 0, '[]'::jsonb, now(), now(), 1)",
            "import_batches" =>
                "INSERT INTO financial.import_batches " +
                "(id, tenant_id, file_name, status, total_rows, imported_rows, duplicate_rows, error_rows, preview_truncated, created_at, updated_at, version) " +
                "VALUES (@id, @t, 'x.csv', 'Pending', 0, 0, 0, 0, false, now(), now(), 1)",
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("t", tenantId);
        await cmd.ExecuteNonQueryAsync();
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
