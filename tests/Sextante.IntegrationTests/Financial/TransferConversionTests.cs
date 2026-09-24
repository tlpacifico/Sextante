using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 grupo 3 — <c>POST /api/financial/transactions/{id}/convert-to-transfer</c>:
/// converter uma transação Regular existente numa perna de transferência,
/// ligando a uma contraparte já existente ou criando-a. Separado de
/// <see cref="TransfersTests"/> (ver a nota nessa classe) para manter o
/// número de signups por classe bem abaixo do limite do rate limiter de
/// auth (achado da revisão final do grupo 3).
/// </summary>
public sealed class TransferConversionTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public TransferConversionTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Convert_existing_regular_transaction_to_transfer_linking_an_existing_counterpart()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-convert-link");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var expenseCategory = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var incomeCategory = await CreateCategoryAsync(client, "Diversos In", kind: 1);

        var expenseId = await CreateTransactionAsync(client, accountA, expenseCategory, 100m, "saída A");
        var incomeId = await CreateTransactionAsync(client, accountB, incomeCategory, 100m, "entrada B");

        var convertResponse = await client.PostAsJsonAsync(
            $"/api/financial/transactions/{expenseId}/convert-to-transfer",
            new { counterpartAccountId = accountB, counterpartTransactionId = incomeId });

        convertResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var transfer = await convertResponse.Content.ReadFromJsonAsync<TransferRow>();
        transfer!.OutLeg.Id.Should().Be(expenseId);
        transfer.InLeg.Id.Should().Be(incomeId);
        transfer.OutLeg.Kind.Should().Be("Transfer");
        transfer.InLeg.Kind.Should().Be("Transfer");
        transfer.OutLeg.CategoryId.Should().BeNull();
        transfer.InLeg.CategoryId.Should().BeNull();
        transfer.OutLeg.CounterpartAccountId.Should().Be(accountB);
        transfer.InLeg.CounterpartAccountId.Should().Be(accountA);
    }

    [Fact]
    public async Task Convert_existing_regular_transaction_to_transfer_creates_the_counterpart_when_none_given()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-convert-new");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var expenseCategory = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var expenseId = await CreateTransactionAsync(client, accountA, expenseCategory, 100m, "saída A");

        var convertResponse = await client.PostAsJsonAsync(
            $"/api/financial/transactions/{expenseId}/convert-to-transfer",
            new { counterpartAccountId = accountB, counterpartTransactionId = (Guid?)null });

        convertResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var transfer = await convertResponse.Content.ReadFromJsonAsync<TransferRow>();
        transfer!.OutLeg.Id.Should().Be(expenseId);
        transfer.InLeg.AccountId.Should().Be(accountB);
        transfer.InLeg.Amount.Amount.Should().Be(100m);
        transfer.OutLeg.CounterpartAccountId.Should().Be(accountB);
        transfer.InLeg.CounterpartAccountId.Should().Be(accountA);
        transfer.InLeg.Direction.Should().Be("Inflow");
    }

    [Fact]
    public async Task Convert_cross_currency_without_counterpart_transaction_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-convert-fx");
        var accountA = await CreateAccountAsync(client, "EUR");
        var accountB = await CreateAccountAsync(client, "USD");
        var expenseCategory = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var expenseId = await CreateTransactionAsync(client, accountA, expenseCategory, 100m, "saída EUR");

        var convertResponse = await client.PostAsJsonAsync(
            $"/api/financial/transactions/{expenseId}/convert-to-transfer",
            new { counterpartAccountId = accountB, counterpartTransactionId = (Guid?)null });

        convertResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Convert_linking_a_transaction_from_the_wrong_account_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-convert-wrong-acc");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var accountC = await CreateAccountAsync(client);
        var expenseCategory = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var incomeCategory = await CreateCategoryAsync(client, "Diversos In", kind: 1);

        var expenseId = await CreateTransactionAsync(client, accountA, expenseCategory, 100m, "saída A");
        // A candidata está em C, mas o pedido diz que a contraparte é B.
        var wrongAccountTransactionId = await CreateTransactionAsync(client, accountC, incomeCategory, 100m, "entrada C");

        var convertResponse = await client.PostAsJsonAsync(
            $"/api/financial/transactions/{expenseId}/convert-to-transfer",
            new { counterpartAccountId = accountB, counterpartTransactionId = wrongAccountTransactionId });

        convertResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Convert_linking_a_non_regular_counterpart_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-convert-nonregular");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var accountC = await CreateAccountAsync(client);
        var expenseCategory = await CreateCategoryAsync(client, "Diversos", kind: 0);

        var expenseId = await CreateTransactionAsync(client, accountA, expenseCategory, 100m, "saída A");

        // Cria um par de transferência entre B e C — a perna em B deixa de ser Regular.
        var existingTransferResponse = await client.PostAsJsonAsync("/api/financial/transfers", new
        {
            fromAccountId = accountB,
            toAccountId = accountC,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            amountOut = 50m,
            amountIn = (decimal?)null,
            description = "outra transferência",
        });
        existingTransferResponse.EnsureSuccessStatusCode();
        var existingTransfer = await existingTransferResponse.Content.ReadFromJsonAsync<TransferRow>();

        var convertResponse = await client.PostAsJsonAsync(
            $"/api/financial/transactions/{expenseId}/convert-to-transfer",
            new { counterpartAccountId = accountB, counterpartTransactionId = existingTransfer!.OutLeg.Id });

        convertResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Convert_linking_a_transaction_with_the_same_direction_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-convert-samedir");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var expenseCategoryA = await CreateCategoryAsync(client, "Diversos A", kind: 0);
        var expenseCategoryB = await CreateCategoryAsync(client, "Diversos B", kind: 0);

        var expenseId = await CreateTransactionAsync(client, accountA, expenseCategoryA, 100m, "saída A");
        var otherExpenseId = await CreateTransactionAsync(client, accountB, expenseCategoryB, 100m, "saída B");

        var convertResponse = await client.PostAsJsonAsync(
            $"/api/financial/transactions/{expenseId}/convert-to-transfer",
            new { counterpartAccountId = accountB, counterpartTransactionId = otherExpenseId });

        convertResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Convert_linking_a_transaction_with_mismatched_amount_in_the_same_currency_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-convert-amount");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var expenseCategory = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var incomeCategory = await CreateCategoryAsync(client, "Diversos In", kind: 1);

        var expenseId = await CreateTransactionAsync(client, accountA, expenseCategory, 100m, "saída A");
        var incomeId = await CreateTransactionAsync(client, accountB, incomeCategory, 75m, "entrada B com valor diferente");

        var convertResponse = await client.PostAsJsonAsync(
            $"/api/financial/transactions/{expenseId}/convert-to-transfer",
            new { counterpartAccountId = accountB, counterpartTransactionId = incomeId });

        convertResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Convert_a_transaction_that_is_already_a_transfer_leg_returns_400()
    {
        // Review Focus #5 do plano do grupo 3, ao nível HTTP (o domínio já
        // estava coberto por TransactionTransferTests).
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-convert-already-xfer");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var accountC = await CreateAccountAsync(client);

        var existingTransferResponse = await client.PostAsJsonAsync("/api/financial/transfers", new
        {
            fromAccountId = accountA,
            toAccountId = accountB,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            amountOut = 50m,
            amountIn = (decimal?)null,
            description = "já é transferência",
        });
        existingTransferResponse.EnsureSuccessStatusCode();
        var existingTransfer = await existingTransferResponse.Content.ReadFromJsonAsync<TransferRow>();

        var convertResponse = await client.PostAsJsonAsync(
            $"/api/financial/transactions/{existingTransfer!.OutLeg.Id}/convert-to-transfer",
            new { counterpartAccountId = accountC, counterpartTransactionId = (Guid?)null });

        convertResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Updating_a_transfer_keeps_the_gap_between_leg_dates()
    {
        // Revisão da phase (I2) — o import deixa cada perna na data do seu
        // extrato (ex.: conta 11/09, cartão 14/09). Editar a transferência não
        // pode pôr as duas na mesma data.
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "xfer-update-gap");
        var accountA = await CreateAccountAsync(client);
        var accountB = await CreateAccountAsync(client);
        var expenseCategory = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var incomeCategory = await CreateCategoryAsync(client, "Diversos In", kind: 1);
        var outDate = DateTimeOffset.UtcNow.Date.AddDays(-10);
        var expenseId = await CreateAsync(client, "/api/financial/transactions", new
        {
            accountId = accountA,
            categoryId = expenseCategory,
            occurredAt = new DateTimeOffset(outDate, TimeSpan.Zero),
            amount = 100m,
            currency = (string?)null,
            description = "saída A",
            tags = (string[]?)null,
        });
        var incomeId = await CreateAsync(client, "/api/financial/transactions", new
        {
            accountId = accountB,
            categoryId = incomeCategory,
            occurredAt = new DateTimeOffset(outDate.AddDays(3), TimeSpan.Zero),
            amount = 100m,
            currency = (string?)null,
            description = "entrada B",
            tags = (string[]?)null,
        });
        var convert = await client.PostAsJsonAsync(
            $"/api/financial/transactions/{expenseId}/convert-to-transfer",
            new { counterpartAccountId = accountB, counterpartTransactionId = incomeId });
        convert.EnsureSuccessStatusCode();
        var transfer = (await convert.Content.ReadFromJsonAsync<TransferRow>())!;

        async Task<TransferRow> PutAsync(DateTimeOffset occurredAt, string description)
        {
            var put = await client.PutAsJsonAsync($"/api/financial/transfers/{transfer.TransferId}", new
            {
                fromAccountId = accountA,
                toAccountId = accountB,
                occurredAt,
                amountOut = 100m,
                amountIn = (decimal?)null,
                description,
            });
            put.EnsureSuccessStatusCode();
            return (await put.Content.ReadFromJsonAsync<TransferRow>())!;
        }

        var sameDate = await PutAsync(new DateTimeOffset(outDate, TimeSpan.Zero), "só a descrição");
        sameDate.OutLeg.OccurredAt.UtcDateTime.Date.Should().Be(outDate);
        sameDate.InLeg.OccurredAt.UtcDateTime.Date.Should().Be(outDate.AddDays(3));

        var moved = await PutAsync(new DateTimeOffset(outDate.AddDays(1), TimeSpan.Zero), "um dia depois");
        moved.OutLeg.OccurredAt.UtcDateTime.Date.Should().Be(outDate.AddDays(1));
        moved.InLeg.OccurredAt.UtcDateTime.Date.Should().Be(outDate.AddDays(4));
    }

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

    private sealed record IdRow(Guid Id);

    private sealed record MoneyValue(decimal Amount, string Currency);

    private sealed record TransactionRow(
        Guid Id,
        Guid AccountId,
        DateTimeOffset OccurredAt,
        Guid? CategoryId,
        MoneyValue Amount,
        string? Description,
        string Direction,
        string Kind,
        Guid? TransferId,
        Guid? CounterpartAccountId);

    private sealed record TransferRow(Guid TransferId, TransactionRow OutLeg, TransactionRow InLeg);
}
