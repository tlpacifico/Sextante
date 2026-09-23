using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 grupo 3 — comandos de transferência (criar/atualizar/apagar) e
/// as regras de forma/multi-tenancy. Os testes de "converter uma transação
/// existente" vivem em <see cref="TransferConversionTests"/> — separados
/// para que cada classe (fixture própria, portanto rate limiter de auth
/// próprio) fique bem abaixo do limite de 30 signups/minuto por IP
/// (achado da revisão final: esta classe sozinha excedia o limite).
/// </summary>
public sealed class TransfersTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public TransfersTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_transfer_same_currency_creates_two_linked_legs_with_opposite_direction_and_amount()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-basic");
        var fromId = await CreateAccountAsync(client);
        var toId = await CreateAccountAsync(client);

        var response = await CreateTransferAsync(client, fromId, toId, 100m, null, "Transferência teste");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var transfer = await response.Content.ReadFromJsonAsync<TransferRow>();

        transfer!.TransferId.Should().NotBeEmpty();
        transfer.OutLeg.AccountId.Should().Be(fromId);
        transfer.InLeg.AccountId.Should().Be(toId);
        transfer.OutLeg.Direction.Should().Be("Outflow");
        transfer.InLeg.Direction.Should().Be("Inflow");
        transfer.OutLeg.Kind.Should().Be("Transfer");
        transfer.InLeg.Kind.Should().Be("Transfer");
        transfer.OutLeg.TransferId.Should().Be(transfer.TransferId);
        transfer.InLeg.TransferId.Should().Be(transfer.TransferId);
        transfer.OutLeg.Amount.Amount.Should().Be(100m);
        transfer.InLeg.Amount.Amount.Should().Be(100m);
        transfer.OutLeg.CategoryId.Should().BeNull();
        transfer.InLeg.CategoryId.Should().BeNull();
        // Ambas as pernas já são conhecidas no momento da resposta — não deve
        // ser preciso um GET extra para saber a conta contraparte.
        transfer.OutLeg.CounterpartAccountId.Should().Be(toId);
        transfer.InLeg.CounterpartAccountId.Should().Be(fromId);
    }

    [Fact]
    public async Task Create_transfer_cross_currency_requires_amount_in_and_records_it_on_the_in_leg()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-fx");
        var fromId = await CreateAccountAsync(client, "EUR");
        var toId = await CreateAccountAsync(client, "USD");
        await SeedExchangeRateAsync("EUR", "USD", 1.09m);

        var withoutAmountIn = await CreateTransferAsync(client, fromId, toId, 100m, null, "fx sem amountIn");
        withoutAmountIn.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var withAmountIn = await CreateTransferAsync(client, fromId, toId, 100m, 120m, "fx com amountIn");
        withAmountIn.StatusCode.Should().Be(HttpStatusCode.Created);
        var transfer = await withAmountIn.Content.ReadFromJsonAsync<TransferRow>();

        transfer!.OutLeg.Amount.Currency.Should().Be("EUR");
        transfer.OutLeg.Amount.Amount.Should().Be(100m);
        transfer.InLeg.Amount.Currency.Should().Be("USD");
        transfer.InLeg.Amount.Amount.Should().Be(120m);
    }

    [Fact]
    public async Task Create_transfer_to_nonexistent_account_creates_no_leg_at_all()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-404acc");
        var fromId = await CreateAccountAsync(client);

        var response = await CreateTransferAsync(client, fromId, Guid.NewGuid(), 50m, null, "conta inexistente");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await client.GetFromJsonAsync<TxPage>($"/api/financial/transactions?accountIds={fromId}");
        list!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_transfer_to_another_tenants_account_returns_404_and_creates_nothing()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-tenant-a");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-tenant-b");
        var fromId = await CreateAccountAsync(clientA);
        var otherTenantAccountId = await CreateAccountAsync(clientB);

        var response = await CreateTransferAsync(clientA, fromId, otherTenantAccountId, 50m, null, "cross-tenant");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await clientA.GetFromJsonAsync<TxPage>($"/api/financial/transactions?accountIds={fromId}");
        list!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_transfer_same_account_for_both_sides_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-same-acc");
        var accountId = await CreateAccountAsync(client);

        var response = await CreateTransferAsync(client, accountId, accountId, 50m, null, "mesma conta");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_transfer_with_empty_account_ids_returns_400_from_validator()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-validator");

        var response = await CreateTransferAsync(client, Guid.Empty, Guid.Empty, 50m, null, "ids vazios");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("errors");
    }

    [Fact]
    public async Task Summary_and_budgets_are_unchanged_by_a_transfer_but_account_balances_change()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-summary");
        var fromId = await CreateAccountAsync(client, "EUR", 500m);
        var toId = await CreateAccountAsync(client, "EUR", 500m);

        var range = "dateFrom=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))
            + "&dateTo=" + Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));

        var summaryBefore = await client.GetFromJsonAsync<JsonElement>($"/api/financial/transactions/summary?{range}");

        var create = await CreateTransferAsync(client, fromId, toId, 100m, null, "movimento entre contas");
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var summaryAfter = await client.GetFromJsonAsync<JsonElement>($"/api/financial/transactions/summary?{range}");
        summaryAfter.GetProperty("income").GetProperty("amount").GetDecimal()
            .Should().Be(summaryBefore.GetProperty("income").GetProperty("amount").GetDecimal());
        summaryAfter.GetProperty("expense").GetProperty("amount").GetDecimal()
            .Should().Be(summaryBefore.GetProperty("expense").GetProperty("amount").GetDecimal());

        var accounts = await client.GetFromJsonAsync<List<AccountRow>>("/api/financial/accounts");
        accounts.Should().ContainSingle(a => a.Id == fromId).Which.CurrentBalance.Amount.Should().Be(400m);
        accounts.Should().ContainSingle(a => a.Id == toId).Which.CurrentBalance.Amount.Should().Be(600m);
    }

    [Fact]
    public async Task Update_transfer_changes_amount_on_both_legs()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-update");
        var fromId = await CreateAccountAsync(client);
        var toId = await CreateAccountAsync(client);
        var created = await CreateTransferAsync(client, fromId, toId, 100m, null, "original");
        var transfer = await created.Content.ReadFromJsonAsync<TransferRow>();

        var updateResponse = await client.PutAsJsonAsync($"/api/financial/transfers/{transfer!.TransferId}", new
        {
            fromAccountId = fromId,
            toAccountId = toId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            amountOut = 150m,
            amountIn = (decimal?)null,
            description = "atualizada",
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<TransferRow>();
        updated!.OutLeg.Amount.Amount.Should().Be(150m);
        updated.InLeg.Amount.Amount.Should().Be(150m);
        updated.OutLeg.CounterpartAccountId.Should().Be(toId);
        updated.InLeg.CounterpartAccountId.Should().Be(fromId);
    }

    [Fact]
    public async Task Delete_transfer_soft_deletes_both_legs_and_excludes_them_from_balance()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-delete");
        var fromId = await CreateAccountAsync(client, "EUR", 500m);
        var toId = await CreateAccountAsync(client, "EUR", 500m);
        var created = await CreateTransferAsync(client, fromId, toId, 100m, null, "a apagar");
        var transfer = await created.Content.ReadFromJsonAsync<TransferRow>();

        var deleteResponse = await client.DeleteAsync($"/api/financial/transfers/{transfer!.TransferId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var accounts = await client.GetFromJsonAsync<List<AccountRow>>("/api/financial/accounts");
        accounts.Should().ContainSingle(a => a.Id == fromId).Which.CurrentBalance.Amount.Should().Be(500m);
        accounts.Should().ContainSingle(a => a.Id == toId).Which.CurrentBalance.Amount.Should().Be(500m);
    }

    [Fact]
    public async Task Archiving_or_updating_a_single_leg_via_the_regular_transaction_endpoint_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-single-leg");
        var fromId = await CreateAccountAsync(client);
        var toId = await CreateAccountAsync(client);
        var categoryId = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var created = await CreateTransferAsync(client, fromId, toId, 100m, null, "perna isolada");
        var transfer = await created.Content.ReadFromJsonAsync<TransferRow>();
        var outLegId = transfer!.OutLeg.Id;

        var putResponse = await client.PutAsJsonAsync($"/api/financial/transactions/{outLegId}", new
        {
            accountId = fromId,
            categoryId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            amount = 100m,
            description = "tentativa direta",
            tags = (string[]?)null,
        });
        putResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var deleteResponse = await client.DeleteAsync($"/api/financial/transactions/{outLegId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Transfer_legs_expose_counterpart_account_id_and_regular_transactions_expose_null()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-counterpart");
        var fromId = await CreateAccountAsync(client);
        var toId = await CreateAccountAsync(client);
        var created = await CreateTransferAsync(client, fromId, toId, 100m, null, "com contraparte");
        var transfer = await created.Content.ReadFromJsonAsync<TransferRow>();

        var outLegDetail = await client.GetFromJsonAsync<TransactionRow>($"/api/financial/transactions/{transfer!.OutLeg.Id}");
        var inLegDetail = await client.GetFromJsonAsync<TransactionRow>($"/api/financial/transactions/{transfer.InLeg.Id}");
        outLegDetail!.CounterpartAccountId.Should().Be(toId);
        inLegDetail!.CounterpartAccountId.Should().Be(fromId);

        var page = await client.GetFromJsonAsync<TxPageWithCounterpart>($"/api/financial/transactions?accountIds={fromId}");
        page!.Items.Should().ContainSingle(t => t.Id == transfer.OutLeg.Id).Which.CounterpartAccountId.Should().Be(toId);

        var category = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var regularId = await CreateTransactionAsync(client, fromId, category, 10m, "regular");
        var regularDetail = await client.GetFromJsonAsync<TransactionRow>($"/api/financial/transactions/{regularId}");
        regularDetail!.CounterpartAccountId.Should().BeNull();
    }

    [Fact]
    public async Task Reading_or_deleting_another_tenants_transfer_returns_404()
    {
        var (clientA, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-tenant-read-a");
        var (clientB, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-tenant-read-b");
        var fromId = await CreateAccountAsync(clientA);
        var toId = await CreateAccountAsync(clientA);
        var created = await CreateTransferAsync(clientA, fromId, toId, 50m, null, "tenant A");
        var transfer = await created.Content.ReadFromJsonAsync<TransferRow>();

        var deleteFromB = await clientB.DeleteAsync($"/api/financial/transfers/{transfer!.TransferId}");
        deleteFromB.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> CreateTransferAsync(
        HttpClient client, Guid fromAccountId, Guid toAccountId, decimal amountOut, decimal? amountIn, string description)
        => client.PostAsJsonAsync("/api/financial/transfers", new
        {
            fromAccountId,
            toAccountId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            amountOut,
            amountIn,
            description,
        });

    private static Task<Guid> CreateAccountAsync(HttpClient client, string currency = "EUR", decimal openingBalanceAmount = 0m)
        => CreateAsync(client, "/api/financial/accounts", new
        {
            name = $"Conta {Guid.NewGuid():N}",
            type = 0,
            currency,
            openingBalanceAmount,
        });

    private static Task<Guid> CreateCategoryAsync(HttpClient client, string name, int kind)
        => CreateAsync(client, "/api/financial/categories", new
        {
            name,
            kind,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });

    private static Task<Guid> CreateTransactionAsync(
        HttpClient client, Guid accountId, Guid categoryId, decimal amount, string description)
        => CreateAsync(client, "/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            amount,
            currency = (string?)null,
            description,
            tags = (string[]?)null,
        });

    private static async Task<Guid> CreateAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private async Task SeedExchangeRateAsync(string from, string to, decimal rate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var superConn = _fixture.OpenSuperuserConnection();
        await using var seedCmd = new NpgsqlCommand(
            """
            INSERT INTO shared."exchange_rates"
              ("id", "rate_date", "from_currency", "to_currency", "rate", "source",
               "created_at", "updated_at", "version")
            VALUES
              (@id, @rateDate, @from, @to, @rate, 'manual',
               now(), now(), 1)
            ON CONFLICT ("rate_date", "from_currency", "to_currency") DO NOTHING
            """,
            superConn);
        seedCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        seedCmd.Parameters.AddWithValue("rateDate", today);
        seedCmd.Parameters.AddWithValue("from", from);
        seedCmd.Parameters.AddWithValue("to", to);
        seedCmd.Parameters.AddWithValue("rate", rate);
        await seedCmd.ExecuteNonQueryAsync();
    }

    private sealed record IdRow(Guid Id);

    private sealed record MoneyValue(decimal Amount, string Currency);

    private sealed record AccountRow(Guid Id, string Name, string Type, MoneyValue CurrentBalance);

    private sealed record TransactionRow(
        Guid Id,
        Guid AccountId,
        Guid? CategoryId,
        MoneyValue Amount,
        string? Description,
        string Direction,
        string Kind,
        Guid? TransferId,
        Guid? CounterpartAccountId);

    private sealed record TransferRow(Guid TransferId, TransactionRow OutLeg, TransactionRow InLeg);

    private sealed record TxItem(Guid Id, Guid AccountId);

    private sealed record TxPage(IReadOnlyList<TxItem> Items, string? NextCursor);

    private sealed record TxItemWithCounterpart(Guid Id, Guid AccountId, Guid? CounterpartAccountId);

    private sealed record TxPageWithCounterpart(IReadOnlyList<TxItemWithCounterpart> Items, string? NextCursor);
}
