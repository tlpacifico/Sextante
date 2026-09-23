using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sextante.Modules.Financial.Domain.Accounts;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 §5 (grupo 5) — definições do cartão no CRUD de conta e
/// <c>GET /api/financial/accounts/{id}/credit-card</c> (dívida, disponível,
/// ciclo corrente, extrato anterior e próximo pagamento). As datas esperadas
/// vêm do <see cref="CreditCardCalendar"/> do domínio, não de aritmética à mão.
/// </summary>
public sealed class CreditCardTests : IClassFixture<IdentityIntegrationFixture>
{
    private const short Checking = 0;
    private const short CreditCard = 3;
    private const int ClosingDay = 15;
    private const int DueDay = 5;

    private readonly IdentityIntegrationFixture _fixture;

    public CreditCardTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_credit_card_with_settings_round_trips()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-create");
        var checkingId = await CreateAccountAsync(client, Checking, 0m);

        var cardId = await CreateAccountAsync(client, CreditCard, -500m, creditCard: new
        {
            creditLimit = 2000m,
            statementClosingDay = 20,
            paymentDueDay = 10,
            paymentAccountId = checkingId,
        });

        var account = await client.GetFromJsonAsync<AccountRow>($"/api/financial/accounts/{cardId}");
        account!.CreditCard.Should().NotBeNull();
        account.CreditCard!.CreditLimit.Should().Be(new MoneyValue(2000m, "EUR"));
        account.CreditCard.StatementClosingDay.Should().Be(20);
        account.CreditCard.PaymentDueDay.Should().Be(10);
        account.CreditCard.PaymentAccountId.Should().Be(checkingId);

        var list = await client.GetFromJsonAsync<List<AccountRow>>("/api/financial/accounts");
        list!.Single(a => a.Id == cardId).CreditCard!.StatementClosingDay.Should().Be(20);
        list!.Single(a => a.Id == checkingId).CreditCard.Should().BeNull();
    }

    [Fact]
    public async Task Settings_on_checking_account_are_rejected()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-checking");

        var response = await PostAccountAsync(client, Checking, 0m, new
        {
            creditLimit = 2000m,
            statementClosingDay = 20,
            paymentDueDay = 10,
            paymentAccountId = (Guid?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Payment_account_must_be_a_non_card_account_of_the_tenant()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-payacc");
        var otherCardId = await CreateAccountAsync(client, CreditCard, 0m);

        var toCard = await PostAccountAsync(client, CreditCard, 0m, Settings(otherCardId));
        toCard.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var toUnknown = await PostAccountAsync(client, CreditCard, 0m, Settings(Guid.NewGuid()));
        toUnknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_with_null_settings_removes_them()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-remove");
        var cardId = await CreateAccountAsync(client, CreditCard, 0m, Settings(null));

        var put = await client.PutAsJsonAsync($"/api/financial/accounts/{cardId}", new
        {
            name = "Cartão",
            type = CreditCard,
            creditCard = (object?)null,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var account = await client.GetFromJsonAsync<AccountRow>($"/api/financial/accounts/{cardId}");
        account!.CreditCard.Should().BeNull();
    }

    [Fact]
    public async Task Update_replaces_settings()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-replace");
        var cardId = await CreateAccountAsync(client, CreditCard, 0m, Settings(null));

        var put = await client.PutAsJsonAsync($"/api/financial/accounts/{cardId}", new
        {
            name = "Cartão",
            type = CreditCard,
            creditCard = new { creditLimit = 3500m, statementClosingDay = 31, paymentDueDay = 25, paymentAccountId = (Guid?)null },
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var account = (await put.Content.ReadFromJsonAsync<AccountRow>())!;
        account.CreditCard!.CreditLimit.Amount.Should().Be(3500m);
        account.CreditCard.StatementClosingDay.Should().Be(31);
        account.CreditCard.PaymentDueDay.Should().Be(25);
    }

    [Fact]
    public async Task Changing_type_away_from_credit_card_clears_settings()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-type");
        var cardId = await CreateAccountAsync(client, CreditCard, 0m, Settings(null));

        var put = await client.PutAsJsonAsync($"/api/financial/accounts/{cardId}", new
        {
            name = "Agora conta à ordem",
            type = Checking,
        });

        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var account = await client.GetFromJsonAsync<AccountRow>($"/api/financial/accounts/{cardId}");
        account!.CreditCard.Should().BeNull();
    }

    [Fact]
    public async Task Credit_card_view_hides_payment_account_that_became_a_card()
    {
        // Revisão final do grupo 5: a conta de pagamento é uma soft reference;
        // se deixar de ser elegível, a vista não a mostra como quem paga.
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-view-payer");
        var payerId = await CreateAccountAsync(client, Checking, 0m);
        var cardId = await CreateAccountAsync(client, CreditCard, 0m, Settings(payerId));

        var put = await client.PutAsJsonAsync($"/api/financial/accounts/{payerId}", new
        {
            name = "Agora é cartão",
            type = CreditCard,
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var view = await client.GetFromJsonAsync<CreditCardViewRow>($"/api/financial/accounts/{cardId}/credit-card");
        view!.PaymentAccountId.Should().BeNull();
    }

    [Fact]
    public async Task Credit_card_view_without_settings_returns_balance_only()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-view-empty");
        var cardId = await CreateAccountAsync(client, CreditCard, -300m);

        var view = await client.GetFromJsonAsync<CreditCardViewRow>($"/api/financial/accounts/{cardId}/credit-card");

        view!.CurrentBalance.Amount.Should().Be(-300m);
        view.CurrentDebt.Amount.Should().Be(300m);
        view.Settings.Should().BeNull();
        view.Available.Should().BeNull();
        view.CurrentCycle.Should().BeNull();
        view.PreviousCycle.Should().BeNull();
        view.NextPaymentAmount.Should().BeNull();
        view.NextPaymentDueDate.Should().BeNull();
    }

    [Fact]
    public async Task Credit_card_view_on_checking_account_returns_400()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-view-checking");
        var checkingId = await CreateAccountAsync(client, Checking, 0m);

        var response = await client.GetAsync($"/api/financial/accounts/{checkingId}/credit-card");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Credit_card_view_of_unknown_account_returns_404()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-view-404");

        var response = await client.GetAsync($"/api/financial/accounts/{Guid.NewGuid()}/credit-card");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Credit_card_view_aggregates_cycles_and_next_payment()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-view");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = CreditCardCalendar.CycleContaining(today, ClosingDay, DueDay);
        var previous = CreditCardCalendar.Previous(current, ClosingDay, DueDay);

        var checkingId = await CreateAccountAsync(client, Checking, 5000m, openingBalanceDate: previous.Start.AddDays(-1));
        var cardId = await CreateAccountAsync(
            client, CreditCard, -1000m, openingBalanceDate: previous.Start.AddDays(-1),
            creditCard: new { creditLimit = 2000m, statementClosingDay = ClosingDay, paymentDueDay = DueDay, paymentAccountId = checkingId });

        var expense = await CreateCategoryAsync(client, "Compras", kind: 0);
        var income = await CreateCategoryAsync(client, "Reembolsos", kind: 1);

        var inPreviousCycle = new DateTimeOffset(previous.End.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
        var inCurrentCycle = DateTimeOffset.UtcNow.AddSeconds(-5);

        await CreateTransactionAsync(client, cardId, expense, 200m, inPreviousCycle);
        await CreateTransactionAsync(client, cardId, expense, 100m, inCurrentCycle);
        await CreateTransactionAsync(client, cardId, income, 30m, inCurrentCycle);

        var transfer = await client.PostAsJsonAsync("/api/financial/transfers", new
        {
            fromAccountId = checkingId,
            toAccountId = cardId,
            occurredAt = inCurrentCycle,
            amountOut = 400m,
            amountIn = (decimal?)null,
            description = "Pagamento do cartão",
        });
        transfer.StatusCode.Should().Be(HttpStatusCode.Created);

        var view = await client.GetFromJsonAsync<CreditCardViewRow>($"/api/financial/accounts/{cardId}/credit-card");

        view!.Settings.Should().NotBeNull();
        view.PreviousCycle!.Start.Should().Be(previous.Start);
        view.PreviousCycle.End.Should().Be(previous.End);
        view.PreviousCycle.Spent.Amount.Should().Be(200m);
        view.CurrentCycle!.Start.Should().Be(current.Start);
        view.CurrentCycle.End.Should().Be(current.End);
        view.CurrentCycle.Spent.Amount.Should().Be(70m);
        view.CurrentCycle.PaymentsReceived.Amount.Should().Be(400m);
        view.PreviousClosingDebt!.Amount.Should().Be(1200m);
        view.NextPaymentAmount!.Amount.Should().Be(800m);
        view.NextPaymentDueDate.Should().Be(previous.PaymentDueDate);
        view.CurrentBalance.Amount.Should().Be(-870m);
        view.CurrentDebt.Amount.Should().Be(870m);
        view.Available!.Amount.Should().Be(1130m);
        view.PaymentAccountId.Should().Be(checkingId);
    }

    [Fact]
    public async Task Credit_card_view_discounts_unbilled_installments()
    {
        // Grupo 6 — plano de 600 em 6 com a 1.ª no fecho anterior: 5 × 100
        // ainda por faturar nesse fecho; próximo pagamento = 1 000 − 500.
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-view-ip");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = CreditCardCalendar.CycleContaining(today, ClosingDay, DueDay);
        var previous = CreditCardCalendar.Previous(current, ClosingDay, DueDay);

        var cardId = await CreateAccountAsync(
            client, CreditCard, -1000m, openingBalanceDate: previous.Start.AddDays(-1),
            creditCard: new { creditLimit = 2000m, statementClosingDay = ClosingDay, paymentDueDay = DueDay, paymentAccountId = (Guid?)null });

        var plan = await client.PostAsJsonAsync("/api/financial/installment-plans", new
        {
            accountId = cardId,
            purchaseTransactionId = (Guid?)null,
            purchaseDate = previous.Start.ToString("yyyy-MM-dd"),
            description = "Portátil",
            totalAmount = 600m,
            installmentCount = 6,
            installmentsAlreadyPaid = 0,
            firstInstallmentDate = previous.End.ToString("yyyy-MM-dd"),
            annualRate = (decimal?)null,
        });
        plan.StatusCode.Should().Be(HttpStatusCode.Created, await plan.Content.ReadAsStringAsync());

        var view = await client.GetFromJsonAsync<CreditCardViewRow>($"/api/financial/accounts/{cardId}/credit-card");

        view!.PreviousClosingDebt!.Amount.Should().Be(1000m);
        view.UnbilledInstallmentsAtPreviousClose!.Amount.Should().Be(500m);
        view.NextPaymentAmount!.Amount.Should().Be(500m);
    }

    [Fact]
    public async Task Credit_card_view_ignores_plans_whose_purchase_is_not_in_the_closed_debt()
    {
        // Revisão profunda do grupo 6 (Crítico): compra feita depois do fecho
        // não está na dívida do fecho — as suas prestações não se descontam.
        // O mesmo para planos manuais com data de compra depois do fecho e
        // para planos cuja compra ligada foi apagada.
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "cc-view-ip-after");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = CreditCardCalendar.CycleContaining(today, ClosingDay, DueDay);
        var previous = CreditCardCalendar.Previous(current, ClosingDay, DueDay);

        var cardId = await CreateAccountAsync(
            client, CreditCard, -500m, openingBalanceDate: previous.Start.AddDays(-1),
            creditCard: new { creditLimit = 5000m, statementClosingDay = ClosingDay, paymentDueDay = DueDay, paymentAccountId = (Guid?)null });
        var expense = await CreateCategoryAsync(client, "Tecnologia", kind: 0);

        async Task PostPlanAsync(Guid? purchaseId, DateOnly purchaseDate, DateOnly first)
        {
            var response = await client.PostAsJsonAsync("/api/financial/installment-plans", new
            {
                accountId = cardId,
                purchaseTransactionId = purchaseId,
                purchaseDate = purchaseDate.ToString("yyyy-MM-dd"),
                description = "Compra",
                totalAmount = 600m,
                installmentCount = 6,
                installmentsAlreadyPaid = 0,
                firstInstallmentDate = first.ToString("yyyy-MM-dd"),
                annualRate = (decimal?)null,
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }

        // Compra ligada, no ciclo corrente (depois do fecho).
        var recentPurchase = await CreateTransactionAsync(client, cardId, expense, 600m, DateTimeOffset.UtcNow.AddSeconds(-5));
        await PostPlanAsync(recentPurchase, today, current.End.AddDays(1));

        // Plano manual com data de compra depois do fecho.
        await PostPlanAsync(null, current.Start, current.End.AddDays(1));

        // Compra ligada, antes do fecho, mas apagada depois.
        var deletedPurchase = await CreateTransactionAsync(
            client, cardId, expense, 600m,
            new DateTimeOffset(previous.Start.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc)));
        await PostPlanAsync(deletedPurchase, previous.Start, previous.End.AddDays(1));
        (await client.DeleteAsync($"/api/financial/transactions/{deletedPurchase}")).EnsureSuccessStatusCode();

        var view = await client.GetFromJsonAsync<CreditCardViewRow>($"/api/financial/accounts/{cardId}/credit-card");

        view!.PreviousClosingDebt!.Amount.Should().Be(500m);
        view.UnbilledInstallmentsAtPreviousClose!.Amount.Should().Be(0m);
        view.NextPaymentAmount!.Amount.Should().Be(500m);
    }

    private static object Settings(Guid? paymentAccountId) => new
    {
        creditLimit = 2000m,
        statementClosingDay = 20,
        paymentDueDay = 10,
        paymentAccountId,
    };

    private static Task<HttpResponseMessage> PostAccountAsync(
        HttpClient client, short type, decimal openingBalanceAmount, object? creditCard = null, DateOnly? openingBalanceDate = null)
        => client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = $"Conta {Guid.NewGuid():N}",
            type,
            currency = "EUR",
            openingBalanceAmount,
            openingBalanceDate,
            creditCard,
        });

    private static async Task<Guid> CreateAccountAsync(
        HttpClient client, short type, decimal openingBalanceAmount, object? creditCard = null, DateOnly? openingBalanceDate = null)
    {
        var response = await PostAccountAsync(client, type, openingBalanceAmount, creditCard, openingBalanceDate);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<Guid> CreateCategoryAsync(HttpClient client, string name, int kind)
    {
        var response = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name,
            kind,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<Guid> CreateTransactionAsync(
        HttpClient client, Guid accountId, Guid categoryId, decimal amount, DateTimeOffset occurredAt)
    {
        var response = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt,
            amount,
            currency = (string?)null,
            description = "test",
            tags = (string[]?)null,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private sealed record IdRow(Guid Id);

    private sealed record MoneyValue(decimal Amount, string Currency);

    private sealed record SettingsRow(MoneyValue CreditLimit, int StatementClosingDay, int PaymentDueDay, Guid? PaymentAccountId);

    private sealed record AccountRow(Guid Id, string Type, MoneyValue CurrentBalance, SettingsRow? CreditCard);

    private sealed record CycleRow(DateOnly Start, DateOnly End, DateOnly PaymentDueDate, MoneyValue Spent, MoneyValue PaymentsReceived);

    private sealed record CreditCardViewRow(
        Guid AccountId,
        MoneyValue CurrentBalance,
        MoneyValue CurrentDebt,
        SettingsRow? Settings,
        MoneyValue? Available,
        CycleRow? CurrentCycle,
        CycleRow? PreviousCycle,
        MoneyValue? PreviousClosingDebt,
        DateOnly? NextPaymentDueDate,
        MoneyValue? NextPaymentAmount,
        Guid? PaymentAccountId,
        MoneyValue? UnbilledInstallmentsAtPreviousClose);
}
