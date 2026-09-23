using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;
using Sextante.Modules.Financial.Infrastructure.Persistence.Migrations;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §2.2 — migration AddAccountOpeningBalanceDate: backfill da
/// data do saldo inicial a partir de <c>created_at</c> e CHECK que restringe
/// saldo inicial negativo a contas do tipo CreditCard. O backfill é
/// idempotente, por isso o teste corre-o outra vez sobre uma linha preparada
/// com um valor propositadamente errado.
/// </summary>
public sealed class AccountOpeningBalanceMigrationTests : IClassFixture<IdentityIntegrationFixture>
{
    private const short Checking = 0;
    private const short CreditCard = 3;

    private readonly IdentityIntegrationFixture _fixture;

    public AccountOpeningBalanceMigrationTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Backfill_sets_opening_balance_date_from_created_at()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mig-obd");
        var accountId = await CreateAccountAsync(client);

        await using var super = _fixture.OpenSuperuserConnection();

        // Data propositadamente errada, como se a coluna viesse com o default.
        await ExecAsync(super, "UPDATE financial.accounts SET opening_balance_date = @d WHERE id = @id",
            ("d", new DateOnly(2000, 1, 1)), ("id", accountId));

        await ExecAsync(super, AccountOpeningBalanceDateBackfill.Sql);

        var (openingBalanceDate, createdAtDate) = await ReadAsync(super, accountId);
        openingBalanceDate.Should().Be(createdAtDate);
    }

    [Fact]
    public async Task Check_rejects_negative_opening_balance_for_non_credit_card_type()
    {
        var (_, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mig-chk-neg");

        await using var super = _fixture.OpenSuperuserConnection();
        var act = async () => await InsertAsync(super, tenantId, Checking, -10m);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
    }

    [Fact]
    public async Task Check_allows_negative_opening_balance_for_credit_card_type()
    {
        var (_, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "mig-chk-cc");

        await using var super = _fixture.OpenSuperuserConnection();
        var act = async () => await InsertAsync(super, tenantId, CreditCard, -500m);

        await act.Should().NotThrowAsync();
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta",
            type = 0,
            currency = "EUR",
            openingBalanceAmount = 0m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task InsertAsync(NpgsqlConnection conn, Guid tenantId, short type, decimal openingBalanceAmount)
    {
        await ExecAsync(
            conn,
            "INSERT INTO financial.accounts " +
            "(id, tenant_id, name, type, currency, opening_balance_amount, opening_balance_currency, opening_balance_date, created_at, updated_at, version) " +
            "VALUES (@id, @t, 'Conta', @ty, 'EUR', @amt, 'EUR', CURRENT_DATE, now(), now(), 1)",
            ("id", Guid.NewGuid()), ("t", tenantId), ("ty", type), ("amt", openingBalanceAmount));
    }

    private static async Task<(DateOnly OpeningBalanceDate, DateOnly CreatedAtDate)> ReadAsync(NpgsqlConnection conn, Guid id)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT opening_balance_date, created_at::date FROM financial.accounts WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1));
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

    private sealed record IdRow(Guid Id);
}
