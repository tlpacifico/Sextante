using System.Net.Http.Json;
using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §5.2 (grupo 5) — migration AddCreditCardSettings: as definições
/// de cartão só existem em contas CreditCard, preenchidas todas ou nenhuma
/// (a conta de pagamento é opcional), com limite positivo na moeda da conta
/// e dias 1–31.
/// </summary>
public sealed class CreditCardSettingsMigrationTests : IClassFixture<IdentityIntegrationFixture>
{
    private const short Checking = 0;
    private const short CreditCard = 3;

    private readonly IdentityIntegrationFixture _fixture;

    public CreditCardSettingsMigrationTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Check_rejects_settings_on_non_credit_card()
    {
        var accountId = await CreateAccountAsync("mig-cc-checking", Checking);

        await ExpectCheckViolationAsync(accountId, "SET " + CompleteSettings);
    }

    [Fact]
    public async Task Check_rejects_partial_settings()
    {
        var accountId = await CreateAccountAsync("mig-cc-partial", CreditCard);

        await ExpectCheckViolationAsync(accountId, "SET credit_card_limit_amount = 2000");
    }

    [Fact]
    public async Task Check_rejects_non_positive_limit()
    {
        var accountId = await CreateAccountAsync("mig-cc-limit", CreditCard);

        await ExpectCheckViolationAsync(accountId, "SET " + CompleteSettings + ", credit_card_limit_amount = 0");
    }

    [Fact]
    public async Task Check_rejects_other_currency()
    {
        var accountId = await CreateAccountAsync("mig-cc-currency", CreditCard);

        await ExpectCheckViolationAsync(accountId, "SET " + CompleteSettings + ", credit_card_limit_currency = 'USD'");
    }

    [Fact]
    public async Task Check_rejects_day_32()
    {
        var accountId = await CreateAccountAsync("mig-cc-day", CreditCard);

        await ExpectCheckViolationAsync(accountId, "SET " + CompleteSettings + ", credit_card_payment_due_day = 32");
    }

    [Fact]
    public async Task Check_accepts_complete_settings_on_credit_card()
    {
        var accountId = await CreateAccountAsync("mig-cc-ok", CreditCard);

        await using var super = _fixture.OpenSuperuserConnection();
        var act = async () => await ExecAsync(super, "UPDATE financial.accounts SET " + CompleteSettings + " WHERE id = @id", accountId);

        await act.Should().NotThrowAsync();
    }

    private const string CompleteSettings =
        "credit_card_limit_amount = 2000, credit_card_limit_currency = 'EUR', " +
        "credit_card_statement_closing_day = 20, credit_card_payment_due_day = 10";

    private async Task ExpectCheckViolationAsync(Guid accountId, string setClause)
    {
        await using var super = _fixture.OpenSuperuserConnection();
        var act = async () => await ExecAsync(super, $"UPDATE financial.accounts {setClause} WHERE id = @id", accountId);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
    }

    private async Task<Guid> CreateAccountAsync(string prefix, short type)
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, prefix);
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta",
            type,
            currency = "EUR",
            openingBalanceAmount = 0m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, Guid id)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed record IdRow(Guid Id);
}
