using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Phase 6.5 grupo 7 (§7.2) — regras de categorização com a ação
/// MarkAsTransfer: CRUD e "reaplicar regras" que converte transações
/// existentes em transferências (liga à contraperna ou cria-a).
/// </summary>
public sealed class CategorizationRuleTransferTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public CategorizationRuleTransferTests(IdentityIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Create_transfer_rule_returns_action_and_target()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rule-xfer-create");
        var card = await CreateAccountAsync(client, "Cartão", type: 3);

        var response = await client.PostAsJsonAsync("/api/financial/categorization-rules", new
        {
            name = "Pagamento cartão",
            pattern = "PAGAMENTO CARTAO",
            matchType = "Contains",
            categoryId = (Guid?)null,
            priority = 50,
            action = "MarkAsTransfer",
            targetAccountId = card,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var rule = await response.Content.ReadFromJsonAsync<RuleRow>();
        rule!.Action.Should().Be("MarkAsTransfer");
        rule.CategoryId.Should().BeNull();
        rule.TargetAccountId.Should().Be(card);
        rule.TargetAccountName.Should().Be("Cartão");

        var listed = await client.GetFromJsonAsync<List<RuleRow>>("/api/financial/categorization-rules");
        listed!.Single(r => r.Id == rule.Id).TargetAccountName.Should().Be("Cartão");
    }

    [Fact]
    public async Task Transfer_rule_requires_target_and_rejects_category()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rule-xfer-invalid");
        var card = await CreateAccountAsync(client, "Cartão", type: 3);
        var category = await CreateCategoryAsync(client, "Diversos", kind: 0);

        var withoutTarget = await client.PostAsJsonAsync("/api/financial/categorization-rules", new
        {
            name = "x", pattern = "x", matchType = "Contains", categoryId = (Guid?)null, priority = 60,
            action = "MarkAsTransfer", targetAccountId = (Guid?)null,
        });
        var withCategory = await client.PostAsJsonAsync("/api/financial/categorization-rules", new
        {
            name = "x", pattern = "x", matchType = "Contains", categoryId = category, priority = 61,
            action = "MarkAsTransfer", targetAccountId = card,
        });

        withoutTarget.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        withCategory.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_target_account_returns_404()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rule-xfer-404");

        var response = await client.PostAsJsonAsync("/api/financial/categorization-rules", new
        {
            name = "x", pattern = "x", matchType = "Contains", categoryId = (Guid?)null, priority = 70,
            action = "MarkAsTransfer", targetAccountId = Guid.NewGuid(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_switches_between_actions()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rule-xfer-update");
        var card = await CreateAccountAsync(client, "Cartão", type: 3);
        var category = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var ruleId = await CreateAsync(client, "/api/financial/categorization-rules", new
        {
            name = "Regra", pattern = "X", matchType = "Contains", categoryId = category, priority = 80,
        });

        var toTransfer = await client.PutAsJsonAsync($"/api/financial/categorization-rules/{ruleId}", new
        {
            name = "Regra", pattern = "X", matchType = "Contains", categoryId = (Guid?)null, priority = 80,
            isActive = true, action = "MarkAsTransfer", targetAccountId = card,
        });
        toTransfer.EnsureSuccessStatusCode();
        var asTransfer = await toTransfer.Content.ReadFromJsonAsync<RuleRow>();
        asTransfer!.Action.Should().Be("MarkAsTransfer");
        asTransfer.CategoryId.Should().BeNull();

        var back = await client.PutAsJsonAsync($"/api/financial/categorization-rules/{ruleId}", new
        {
            name = "Regra", pattern = "X", matchType = "Contains", categoryId = category, priority = 80,
            isActive = true, action = "SetCategory", targetAccountId = (Guid?)null,
        });
        back.EnsureSuccessStatusCode();
        var asCategory = await back.Content.ReadFromJsonAsync<RuleRow>();
        asCategory!.Action.Should().Be("SetCategory");
        asCategory.CategoryId.Should().Be(category);
        asCategory.TargetAccountId.Should().BeNull();
    }

    [Fact]
    public async Task Reapply_links_existing_counterpart()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rule-xfer-link");
        var checking = await CreateAccountAsync(client, "Conta", type: 0);
        var card = await CreateAccountAsync(client, "Cartão", type: 3);
        var expense = await CreateCategoryAsync(client, "Diversos", kind: 0);
        var income = await CreateCategoryAsync(client, "Entradas", kind: 1);
        var when = DateTimeOffset.UtcNow.AddDays(-5);

        var paymentId = await CreateTransactionAsync(client, checking, expense, 450m, "VIS PAGAMENTO CARTAO", when);
        var receivedId = await CreateTransactionAsync(client, card, income, 450m, "PAGAMENTO RECEBIDO", when.AddDays(2));
        await CreateTransferRuleAsync(client, "PAGAMENTO CARTAO", card, priority: 90);

        var response = await client.PostAsync(
            "/api/financial/categorization-rules/reapply?onlyUncategorized=true", content: null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ReapplyRow>();

        result!.TransfersCount.Should().Be(1);
        var payment = await GetTransactionAsync(client, paymentId);
        var received = await GetTransactionAsync(client, receivedId);
        payment.Kind.Should().Be("Transfer");
        received.Kind.Should().Be("Transfer");
        received.TransferId.Should().Be(payment.TransferId);

        var cardTxs = await client.GetFromJsonAsync<TransactionsPage>(
            $"/api/financial/transactions?accountIds={card}&pageSize=50");
        cardTxs!.Items.Should().ContainSingle("liga à receita existente, sem criar contraperna");
    }

    [Fact]
    public async Task Reapply_creates_counterpart_when_none()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "rule-xfer-new");
        var checking = await CreateAccountAsync(client, "Conta", type: 0);
        var card = await CreateAccountAsync(client, "Cartão", type: 3);
        var expense = await CreateCategoryAsync(client, "Diversos", kind: 0);

        var paymentId = await CreateTransactionAsync(
            client, checking, expense, 450m, "VIS PAGAMENTO CARTAO", DateTimeOffset.UtcNow.AddDays(-2));
        await CreateTransferRuleAsync(client, "PAGAMENTO CARTAO", card, priority: 91);

        var response = await client.PostAsync(
            "/api/financial/categorization-rules/reapply?onlyUncategorized=true", content: null);
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync<ReapplyRow>())!.TransfersCount.Should().Be(1);

        var payment = await GetTransactionAsync(client, paymentId);
        payment.Kind.Should().Be("Transfer");
        payment.CounterpartAccountId.Should().Be(card);
    }

    internal static async Task<Guid> CreateTransferRuleAsync(HttpClient client, string pattern, Guid target, int priority)
        => await CreateAsync(client, "/api/financial/categorization-rules", new
        {
            name = $"Transferência {pattern}",
            pattern,
            matchType = "Contains",
            categoryId = (Guid?)null,
            priority,
            action = "MarkAsTransfer",
            targetAccountId = target,
        });

    private static Task<Guid> CreateAccountAsync(HttpClient client, string name, short type, string currency = "EUR")
        => CreateAsync(client, "/api/financial/accounts", new
        {
            name,
            type,
            currency,
            openingBalanceAmount = 0m,
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
        HttpClient client, Guid accountId, Guid categoryId, decimal amount, string description, DateTimeOffset occurredAt)
        => CreateAsync(client, "/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt,
            amount,
            currency = (string?)null,
            description,
            tags = (string[]?)null,
        });

    private static async Task<TransactionRow> GetTransactionAsync(HttpClient client, Guid id)
        => (await client.GetFromJsonAsync<TransactionRow>($"/api/financial/transactions/{id}"))!;

    private static async Task<Guid> CreateAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST {url} falhou ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private sealed record IdRow(Guid Id);

    private sealed record RuleRow(
        Guid Id, string Action, Guid? CategoryId, Guid? TargetAccountId, string? TargetAccountName);

    private sealed record ReapplyRow(int TotalProcessed, int CategorizedCount, int UnchangedCount, int TransfersCount);

    private sealed record TransactionRow(
        Guid Id, Guid AccountId, string Kind, Guid? TransferId, Guid? CounterpartAccountId);

    private sealed record TransactionsPage(List<TransactionRow> Items);
}
