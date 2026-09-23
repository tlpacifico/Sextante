using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §7.2 (grupo 7) — migration AddImportTransfers: ação nas regras
/// de categorização (categoria XOR conta alvo) e conta de destino no lote.
/// </summary>
public sealed class ImportTransfersMigrationTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public ImportTransfersMigrationTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Existing_rules_default_to_SetCategory()
    {
        var (_, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "it-mig-default");
        await using var conn = _fixture.OpenSuperuserConnection();
        var id = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO financial.categorization_rules " +
            "(id, tenant_id, name, pattern, match_type, category_id, priority, is_active, created_at, updated_at, version) " +
            "VALUES (@id, @t, 'x', 'x', 'Contains', @c, 999, true, now(), now(), 1)", conn))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("t", tenantId);
            insert.Parameters.AddWithValue("c", Guid.NewGuid());
            await insert.ExecuteNonQueryAsync();
        }

        await using var read = new NpgsqlCommand(
            "SELECT action FROM financial.categorization_rules WHERE id = @id", conn);
        read.Parameters.AddWithValue("id", id);

        Convert.ToInt32(await read.ExecuteScalarAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(1, true, true)]   // transferência com categoria
    [InlineData(1, false, false)] // transferência sem conta alvo
    [InlineData(0, false, false)] // categoria sem categoria
    [InlineData(0, true, true)]   // categoria com conta alvo
    public async Task Check_rejects_inconsistent_action_fields(int action, bool withCategory, bool withTarget)
    {
        var (_, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "it-mig-chk");
        await using var conn = _fixture.OpenSuperuserConnection();

        var act = async () => await InsertRuleAsync(conn, tenantId, action, withCategory, withTarget);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("chk_categorization_rules_action");
    }

    [Fact]
    public async Task Transfer_rule_without_category_is_accepted()
    {
        var (_, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "it-mig-ok");
        await using var conn = _fixture.OpenSuperuserConnection();

        var act = async () => await InsertRuleAsync(conn, tenantId, action: 1, withCategory: false, withTarget: true);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Import_batches_accept_account_id()
    {
        var (_, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "it-mig-batch");
        await using var conn = _fixture.OpenSuperuserConnection();
        var id = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO financial.import_batches " +
            "(id, tenant_id, account_id, file_name, status, total_rows, imported_rows, duplicate_rows, error_rows, preview_truncated, created_at, updated_at, version) " +
            "VALUES (@id, @t, @a, 'x.csv', 'Pending', 0, 0, 0, 0, false, now(), now(), 1)", conn))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("t", tenantId);
            insert.Parameters.AddWithValue("a", accountId);
            await insert.ExecuteNonQueryAsync();
        }

        await using var read = new NpgsqlCommand("SELECT account_id FROM financial.import_batches WHERE id = @id", conn);
        read.Parameters.AddWithValue("id", id);

        ((Guid)(await read.ExecuteScalarAsync())!).Should().Be(accountId);
    }

    private static async Task InsertRuleAsync(
        NpgsqlConnection conn, Guid tenantId, int action, bool withCategory, bool withTarget)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO financial.categorization_rules " +
            "(id, tenant_id, name, pattern, match_type, action, category_id, target_account_id, priority, is_active, created_at, updated_at, version) " +
            "VALUES (@id, @t, 'x', 'x', 'Contains', @action, @c, @a, 999, true, now(), now(), 1)", conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("action", (short)action);
        cmd.Parameters.AddWithValue("c", withCategory ? Guid.NewGuid() : DBNull.Value);
        cmd.Parameters.AddWithValue("a", withTarget ? Guid.NewGuid() : DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
