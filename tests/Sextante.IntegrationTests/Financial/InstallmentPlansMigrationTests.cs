using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §6.2 (grupo 6) — migration AddInstallmentPlans: tabela
/// tenant-owned com RLS + FORCE, sentinel CHECK, CHECKs de domínio e uma só
/// compra por plano ativo.
/// </summary>
public sealed class InstallmentPlansMigrationTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public InstallmentPlansMigrationTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Table_has_rls_enabled_and_forced()
    {
        await using var conn = _fixture.OpenSuperuserConnection();
        await using var cmd = new NpgsqlCommand(
            "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class c " +
            "JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE n.nspname = 'financial' AND c.relname = 'installment_plans'",
            conn);

        ((bool)(await cmd.ExecuteScalarAsync())!).Should().BeTrue();
    }

    [Fact]
    public async Task App_role_sees_only_its_tenant_rows()
    {
        var (_, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-rls-a");
        var (_, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-rls-b");

        await using (var super = _fixture.OpenSuperuserConnection())
        {
            await InsertAsync(super, tenantA);
            await InsertAsync(super, tenantB);
        }

        await using var conn = _fixture.OpenAppConnection();
        await SetTenantAsync(conn, tenantA);
        await using var read = new NpgsqlCommand(
            "SELECT count(*) FROM financial.installment_plans WHERE tenant_id IN (@a, @b)", conn);
        read.Parameters.AddWithValue("a", tenantA);
        read.Parameters.AddWithValue("b", tenantB);

        ((long)(await read.ExecuteScalarAsync())!).Should().Be(1);
    }

    [Fact]
    public async Task Cross_tenant_insert_is_rejected()
    {
        var (_, tenantA, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-w-a");
        var (_, tenantB, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-w-b");

        await using var conn = _fixture.OpenAppConnection();
        await SetTenantAsync(conn, tenantA);

        var act = async () => await InsertAsync(conn, tenantB);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task Sentinel_tenant_id_is_rejected()
    {
        await using var conn = _fixture.OpenSuperuserConnection();

        var act = async () => await InsertAsync(conn, new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("installment_plans_tenant_id_not_sentinel");
    }

    [Theory]
    [InlineData("installment_count = 1", "chk_installment_plans_count")]
    [InlineData("installments_already_paid = installment_count", "chk_installment_plans_already_paid")]
    [InlineData("total_amount = 0", "chk_installment_plans_total_positive")]
    [InlineData("annual_rate = 101", "chk_installment_plans_annual_rate")]
    public async Task Checks_reject_invalid_values(string set, string constraint)
    {
        var (_, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-chk");
        await using var conn = _fixture.OpenSuperuserConnection();
        var id = await InsertAsync(conn, tenantId);

        var act = async () =>
        {
            await using var cmd = new NpgsqlCommand($"UPDATE financial.installment_plans SET {set} WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task One_active_plan_per_purchase()
    {
        var (_, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "ip-uq");
        var purchaseId = Guid.NewGuid();
        await using var conn = _fixture.OpenSuperuserConnection();

        var first = await InsertAsync(conn, tenantId, purchaseId);
        var duplicate = async () => await InsertAsync(conn, tenantId, purchaseId);
        (await duplicate.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23505");

        await using (var archive = new NpgsqlCommand(
            "UPDATE financial.installment_plans SET deleted_at = now() WHERE id = @id", conn))
        {
            archive.Parameters.AddWithValue("id", first);
            await archive.ExecuteNonQueryAsync();
        }

        var afterArchive = async () => await InsertAsync(conn, tenantId, purchaseId);
        await afterArchive.Should().NotThrowAsync();
    }

    private static async Task<Guid> InsertAsync(NpgsqlConnection conn, Guid tenantId, Guid? purchaseId = null)
    {
        var id = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO financial.installment_plans " +
            "(id, tenant_id, account_id, purchase_transaction_id, purchase_date, description, total_amount, total_currency, " +
            "installment_count, installments_already_paid, first_installment_date, annual_rate, created_at, updated_at, version) " +
            "VALUES (@id, @t, @a, @p, CURRENT_DATE, 'Plano', 600, 'EUR', 6, 0, CURRENT_DATE, NULL, now(), now(), 1)",
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("a", Guid.NewGuid());
        cmd.Parameters.AddWithValue("p", (object?)purchaseId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task SetTenantAsync(NpgsqlConnection conn, Guid tenantId)
    {
        await using var set = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @t, false)", conn);
        set.Parameters.AddWithValue("t", tenantId.ToString());
        await set.ExecuteScalarAsync();
    }
}
