using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using Sextante.Modules.Financial.Infrastructure.Persistence.Migrations;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §1.2 — migration AddTransactionDirectionAndKind: backfill da
/// direção a partir da categoria (incluindo arquivadas), fim do sentinel
/// Guid.Empty e CHECKs de coerência entre kind, categoria e transfer_id.
/// O backfill é idempotente, por isso o teste corre-o outra vez sobre
/// linhas preparadas com valores errados.
/// </summary>
public sealed class TransactionDirectionMigrationTests : IClassFixture<IdentityIntegrationFixture>
{
    private const short Inflow = 0;
    private const short Outflow = 1;

    private readonly IdentityIntegrationFixture _fixture;

    public TransactionDirectionMigrationTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Backfill_sets_direction_from_category_kind_including_archived_and_clears_sentinel()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mig-dir");
        var accountId = await CreateAsync(client, "/api/financial/accounts", new
        {
            name = "Conta",
            type = 0,
            currency = "EUR",
            openingBalanceAmount = 0m,
        });
        var income = await CreateCategoryAsync(client, "Salário", kind: 1);
        var expense = await CreateCategoryAsync(client, "Supermercado", kind: 0);

        await using var super = _fixture.OpenSuperuserConnection();
        await ExecAsync(super, "UPDATE financial.categories SET deleted_at = now() WHERE id = @id", ("id", income));

        // Direções propositadamente erradas, como se a coluna viesse com o default.
        var incomeTx = await InsertAsync(super, tenantId, accountId, income, direction: Outflow);
        var expenseTx = await InsertAsync(super, tenantId, accountId, expense, direction: Inflow);
        var sentinelTx = await InsertAsync(super, tenantId, accountId, Guid.Empty, direction: Inflow);

        await ExecAsync(super, TransactionDirectionBackfill.Sql);

        (await ReadAsync(super, incomeTx)).Should().Be((income, Inflow));
        (await ReadAsync(super, expenseTx)).Should().Be((expense, Outflow));
        (await ReadAsync(super, sentinelTx)).Should().Be(((Guid?)null, Outflow));
    }

    [Fact]
    public async Task Backfill_leaves_rows_with_dangling_category_as_outflow()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mig-dangling");
        var accountId = await CreateAsync(client, "/api/financial/accounts", new
        {
            name = "Conta",
            type = 0,
            currency = "EUR",
            openingBalanceAmount = 0m,
        });

        await using var super = _fixture.OpenSuperuserConnection();
        var dangling = Guid.NewGuid();
        var tx = await InsertAsync(super, tenantId, accountId, dangling, direction: Outflow);

        await ExecAsync(super, TransactionDirectionBackfill.Sql);

        (await ReadAsync(super, tx)).Should().Be(((Guid?)dangling, Outflow));
    }

    [Theory]
    [InlineData((short)1, false, false)] // Transfer sem transfer_id
    [InlineData((short)2, true, false)]  // Adjustment com categoria
    [InlineData((short)0, true, true)]   // Regular com transfer_id
    [InlineData((short)3, false, false)] // kind desconhecido
    public async Task Check_constraints_reject_invalid_combinations(short kind, bool withCategory, bool withTransfer)
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, $"mig-chk-{kind}");
        var accountId = await CreateAsync(client, "/api/financial/accounts", new
        {
            name = "Conta",
            type = 0,
            currency = "EUR",
            openingBalanceAmount = 0m,
        });

        await using var super = _fixture.OpenSuperuserConnection();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO financial.transactions " +
            "(id, tenant_id, account_id, category_id, occurred_at, amount, currency, tags, created_at, updated_at, version, direction, kind, transfer_id) " +
            "VALUES (@id, @t, @a, @c, now(), 10, 'EUR', '[]'::jsonb, now(), now(), 1, 1, @k, @tr)",
            super);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("a", accountId);
        cmd.Parameters.AddWithValue("c", withCategory ? Guid.NewGuid() : DBNull.Value);
        cmd.Parameters.AddWithValue("k", kind);
        cmd.Parameters.AddWithValue("tr", withTransfer ? Guid.NewGuid() : DBNull.Value);

        var act = async () => await cmd.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
    }

    private static async Task<Guid> InsertAsync(NpgsqlConnection conn, Guid tenantId, Guid accountId, Guid categoryId, short direction)
    {
        var id = Guid.NewGuid();
        await ExecAsync(
            conn,
            "INSERT INTO financial.transactions " +
            "(id, tenant_id, account_id, category_id, occurred_at, amount, currency, tags, created_at, updated_at, version, direction, kind) " +
            "VALUES (@id, @t, @a, @c, now(), 10, 'EUR', '[]'::jsonb, now(), now(), 1, @d, 0)",
            ("id", id), ("t", tenantId), ("a", accountId), ("c", categoryId), ("d", direction));
        return id;
    }

    private static async Task<(Guid? CategoryId, short Direction)> ReadAsync(NpgsqlConnection conn, Guid id)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT category_id, direction FROM financial.transactions WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetInt16(1));
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private static Task<Guid> CreateCategoryAsync(HttpClient client, string name, int kind)
        => CreateAsync(client, "/api/financial/categories", new
        {
            name,
            kind,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });

    private static async Task<Guid> CreateAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private sealed record IdRow(Guid Id);
}
