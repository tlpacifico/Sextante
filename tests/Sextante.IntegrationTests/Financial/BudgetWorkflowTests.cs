using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sextante.Modules.Financial.PublicApi.Events;
using Wolverine;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Pipeline end-to-end Phase 5b: criar Budget → POST transactions
/// → BudgetAlertDispatchHandlers cria alerts em 80% e 100% →
/// acknowledge endpoint resolve.
///
/// Como o Wolverine outbox é assíncrono, despacha-se o handler
/// directamente via DI scope para tornar os asserts determinísticos
/// (mesmo padrão usado em RecurringMaterializationTests).
/// </summary>
public sealed class BudgetWorkflowTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public BudgetWorkflowTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Crossing_80_then_100_creates_two_alerts_then_acknowledged_clears_active()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bw-flow");
        var accountId = await CreateAccountAsync(client);
        var categoryId = await CreateExpenseCategoryAsync(client, "Habitação");

        var year = DateOnly.FromDateTime(DateTime.UtcNow).Year;
        var month = DateOnly.FromDateTime(DateTime.UtcNow).Month;

        // Budget €500.
        var budget = await CreateBudgetAsync(client, categoryId, year, month, 500m, 80);

        // 4 transactions de €100 → 80% exato.
        for (var i = 0; i < 4; i++)
        {
            await CreateTransactionAsync(client, accountId, categoryId, 100m);
        }

        // Dispatch direto: simula o que o subscriber fará quando o
        // outbox processar. Ainda mais determinístico que esperar.
        await DispatchForCategoryAsync(tenantId, categoryId, DateTimeOffset.UtcNow);

        var afterFirstBatch = await client.GetFromJsonAsync<List<AlertRow>>(
            "/api/financial/budgets/alerts/active");
        afterFirstBatch.Should().HaveCount(1);
        afterFirstBatch![0].Threshold.Should().Be(80);
        afterFirstBatch[0].Acknowledged.Should().BeFalse();

        // +€150 → 110%.
        await CreateTransactionAsync(client, accountId, categoryId, 150m);
        await DispatchForCategoryAsync(tenantId, categoryId, DateTimeOffset.UtcNow);

        var afterSecondBatch = await client.GetFromJsonAsync<List<AlertRow>>(
            "/api/financial/budgets/alerts/active");
        afterSecondBatch.Should().HaveCount(2);
        afterSecondBatch.Should().Contain(a => a.Threshold == 80);
        afterSecondBatch.Should().Contain(a => a.Threshold == 100);

        // Acknowledge ambos → vazio.
        foreach (var alert in afterSecondBatch!)
        {
            var ack = await client.PostAsync(
                $"/api/financial/budgets/alerts/{alert.Id}/acknowledge",
                content: null);
            ack.EnsureSuccessStatusCode();
        }

        var afterAck = await client.GetFromJsonAsync<List<AlertRow>>(
            "/api/financial/budgets/alerts/active");
        afterAck.Should().BeEmpty();

        budget.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Reemitting_event_does_not_create_duplicate_alerts()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bw-idem");
        var accountId = await CreateAccountAsync(client);
        var categoryId = await CreateExpenseCategoryAsync(client, "Habitação");

        var year = DateOnly.FromDateTime(DateTime.UtcNow).Year;
        var month = DateOnly.FromDateTime(DateTime.UtcNow).Month;
        await CreateBudgetAsync(client, categoryId, year, month, 500m, 80);

        await CreateTransactionAsync(client, accountId, categoryId, 400m);

        await DispatchForCategoryAsync(tenantId, categoryId, DateTimeOffset.UtcNow);
        await DispatchForCategoryAsync(tenantId, categoryId, DateTimeOffset.UtcNow);
        await DispatchForCategoryAsync(tenantId, categoryId, DateTimeOffset.UtcNow);

        var alerts = await client.GetFromJsonAsync<List<AlertRow>>(
            "/api/financial/budgets/alerts/active");
        alerts.Should().HaveCount(1);
        alerts![0].Threshold.Should().Be(80);
    }

    [Fact]
    public async Task Below_threshold_creates_no_alert()
    {
        var (client, tenantId, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "bw-nope");
        var accountId = await CreateAccountAsync(client);
        var categoryId = await CreateExpenseCategoryAsync(client, "Habitação");

        var year = DateOnly.FromDateTime(DateTime.UtcNow).Year;
        var month = DateOnly.FromDateTime(DateTime.UtcNow).Month;
        await CreateBudgetAsync(client, categoryId, year, month, 1000m, 80);

        await CreateTransactionAsync(client, accountId, categoryId, 100m);
        await DispatchForCategoryAsync(tenantId, categoryId, DateTimeOffset.UtcNow);

        var alerts = await client.GetFromJsonAsync<List<AlertRow>>(
            "/api/financial/budgets/alerts/active");
        alerts.Should().BeEmpty();
    }

    private async Task DispatchForCategoryAsync(Guid tenantId, Guid categoryId, DateTimeOffset occurredAt)
    {
        // Invoca o handler via IMessageBus.InvokeAsync para correr o
        // pipeline Wolverine completo (TenantSettingMiddleware +
        // TenantLoggingMiddleware + handler). Síncrono — não passa pelo
        // outbox, mas executa o mesmo middleware stack que produção.
        await using var scope = _fixture.Factory.Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var @event = new TransactionCreatedIntegrationEvent(
            TransactionId: Guid.NewGuid(),
            TenantId: tenantId,
            AccountId: Guid.NewGuid(),
            CategoryId: categoryId,
            AmountAmount: 0m,
            AmountCurrency: "EUR",
            OccurredAtTransaction: occurredAt,
            OccurredAt: DateTimeOffset.UtcNow);

        await bus.InvokeAsync(@event);
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string currency = "EUR")
    {
        var response = await client.PostAsJsonAsync("/api/financial/accounts", new
        {
            name = "Conta Teste",
            type = 0,
            currency,
            openingBalanceAmount = 0m,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<Guid> CreateExpenseCategoryAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/financial/categories", new
        {
            name,
            kind = 0,
            iconName = "pi-tag",
            colorHex = "#64748B",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdRow>())!.Id;
    }

    private static async Task<BudgetRow> CreateBudgetAsync(
        HttpClient client, Guid categoryId, int year, int month, decimal limit, int threshold)
    {
        var response = await client.PostAsJsonAsync("/api/financial/budgets", new
        {
            categoryId,
            year,
            month,
            limitAmount = limit,
            limitCurrency = "EUR",
            alertThresholdPercent = threshold,
            notes = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BudgetRow>())!;
    }

    private static async Task CreateTransactionAsync(
        HttpClient client, Guid accountId, Guid categoryId, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/financial/transactions", new
        {
            accountId,
            categoryId,
            occurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            amount,
            currency = (string?)null,
            description = "test",
            tags = (string[]?)null,
        });
        response.EnsureSuccessStatusCode();
    }

    private sealed record IdRow(Guid Id);
    private sealed record BudgetRow(Guid Id, decimal LimitAmount, string LimitCurrency);
    private sealed record AlertRow(Guid Id, Guid BudgetId, int Threshold, bool Acknowledged);
}
