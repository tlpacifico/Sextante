using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sextante.Modules.Financial.Application.Features.Budgets;
using Sextante.Modules.Financial.Application.Features.Budgets.Alerts;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.Modules.Financial.PublicApi.Events;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.Budgets;

public sealed class BudgetAlertDispatchHandlersTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid CategoryId = GuidV7.NewId();
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_budget_for_category_does_nothing()
    {
        var fixture = new HandlerFixture();
        var @event = NewCreatedEvent();

        await fixture.Sut.Handle(@event, CancellationToken.None);

        fixture.Alerts.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Below_threshold_does_not_create_alert()
    {
        var budget = NewBudget(limit: 500m, threshold: 80);
        var fixture = new HandlerFixture()
            .WithBudget(budget)
            .WithProgress(spent: 100m, percentUsed: 20m);

        await fixture.Sut.Handle(NewCreatedEvent(), CancellationToken.None);

        fixture.Alerts.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Crossing_default_threshold_creates_80_alert()
    {
        var budget = NewBudget(limit: 500m, threshold: 80);
        var fixture = new HandlerFixture()
            .WithBudget(budget)
            .WithProgress(spent: 400m, percentUsed: 80m);

        await fixture.Sut.Handle(NewCreatedEvent(), CancellationToken.None);

        fixture.Alerts.Store.Values.Should().ContainSingle(a => a.Threshold == 80);
    }

    [Fact]
    public async Task Crossing_100_creates_both_80_and_100_alerts_first_time()
    {
        var budget = NewBudget(limit: 500m, threshold: 80);
        var fixture = new HandlerFixture()
            .WithBudget(budget)
            .WithProgress(spent: 550m, percentUsed: 110m);

        await fixture.Sut.Handle(NewCreatedEvent(), CancellationToken.None);

        fixture.Alerts.Store.Values.Should().HaveCount(2);
        fixture.Alerts.Store.Values.Should().Contain(a => a.Threshold == 80);
        fixture.Alerts.Store.Values.Should().Contain(a => a.Threshold == 100);
    }

    [Fact]
    public async Task When_80_already_emitted_only_100_added_when_threshold_crossed()
    {
        var budget = NewBudget(limit: 500m, threshold: 80);
        var existing80 = BudgetAlert.Create(Tenant, budget.Id, 80, new Money(400m, "EUR"));
        var fixture = new HandlerFixture()
            .WithBudget(budget)
            .WithAlert(existing80)
            .WithProgress(spent: 550m, percentUsed: 110m);

        await fixture.Sut.Handle(NewCreatedEvent(), CancellationToken.None);

        fixture.Alerts.Store.Values.Where(a => a.Threshold == 80).Should().HaveCount(1);
        fixture.Alerts.Store.Values.Where(a => a.Threshold == 100).Should().HaveCount(1);
    }

    [Fact]
    public async Task Re_emitting_event_for_same_transaction_creates_no_new_alerts()
    {
        var budget = NewBudget(limit: 500m, threshold: 80);
        var fixture = new HandlerFixture()
            .WithBudget(budget)
            .WithProgress(spent: 400m, percentUsed: 80m);

        await fixture.Sut.Handle(NewCreatedEvent(), CancellationToken.None);
        await fixture.Sut.Handle(NewCreatedEvent(), CancellationToken.None);

        fixture.Alerts.Store.Values.Where(a => a.Threshold == 80).Should().HaveCount(1);
    }

    [Fact]
    public async Task Custom_threshold_50_alerts_at_50_not_at_80()
    {
        var budget = NewBudget(limit: 500m, threshold: 50);
        var fixture = new HandlerFixture()
            .WithBudget(budget)
            .WithProgress(spent: 250m, percentUsed: 50m);

        await fixture.Sut.Handle(NewCreatedEvent(), CancellationToken.None);

        fixture.Alerts.Store.Values.Should().ContainSingle(a => a.Threshold == 50);
    }

    [Fact]
    public async Task Null_categoryId_skips_dispatch()
    {
        var fixture = new HandlerFixture();
        var @event = new TransactionCreatedIntegrationEvent(
            TransactionId: GuidV7.NewId(),
            TenantId: Tenant.Value,
            AccountId: GuidV7.NewId(),
            CategoryId: null,
            AmountAmount: 100m,
            AmountCurrency: "EUR",
            OccurredAtTransaction: OccurredAt,
            OccurredAt: OccurredAt);

        await fixture.Sut.Handle(@event, CancellationToken.None);

        fixture.Alerts.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_guid_categoryId_skips_dispatch()
    {
        var fixture = new HandlerFixture();
        var @event = NewCreatedEvent() with { CategoryId = Guid.Empty };

        await fixture.Sut.Handle(@event, CancellationToken.None);

        fixture.Alerts.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_event_also_triggers_dispatch()
    {
        var budget = NewBudget(limit: 500m, threshold: 80);
        var fixture = new HandlerFixture()
            .WithBudget(budget)
            .WithProgress(spent: 400m, percentUsed: 80m);

        var updateEvent = new TransactionUpdatedIntegrationEvent(
            TransactionId: GuidV7.NewId(),
            TenantId: Tenant.Value,
            AccountId: GuidV7.NewId(),
            CategoryId: CategoryId,
            AmountAmount: 100m,
            AmountCurrency: "EUR",
            OccurredAtTransaction: OccurredAt,
            OccurredAt: OccurredAt);

        await fixture.Sut.Handle(updateEvent, CancellationToken.None);

        fixture.Alerts.Store.Values.Should().ContainSingle(a => a.Threshold == 80);
    }

    private static TransactionCreatedIntegrationEvent NewCreatedEvent()
        => new(
            TransactionId: GuidV7.NewId(),
            TenantId: Tenant.Value,
            AccountId: GuidV7.NewId(),
            CategoryId: CategoryId,
            AmountAmount: 100m,
            AmountCurrency: "EUR",
            OccurredAtTransaction: OccurredAt,
            OccurredAt: OccurredAt);

    private static Budget NewBudget(decimal limit, int threshold)
        => Budget.Create(Tenant, CategoryId, new BudgetPeriod(2026, 5),
            new Money(limit, "EUR"), threshold, null);

    private sealed class HandlerFixture
    {
        public HandlerFixture()
        {
            Budgets = new StubBudgetRepository();
            Alerts = new StubBudgetAlertRepository();
            Progress = new StubBudgetProgressService();
        }

        public StubBudgetRepository Budgets { get; }
        public StubBudgetAlertRepository Alerts { get; }
        public StubBudgetProgressService Progress { get; }

        public BudgetAlertDispatchHandlers Sut => new(
            Budgets, Alerts, Progress,
            NullLoggerFactory.Instance.CreateLogger<BudgetAlertDispatchHandlers>());

        public HandlerFixture WithBudget(Budget budget)
        {
            Budgets.Store[budget.Id] = budget;
            return this;
        }

        public HandlerFixture WithAlert(BudgetAlert alert)
        {
            Alerts.Store[alert.Id] = alert;
            return this;
        }

        public HandlerFixture WithProgress(decimal spent, decimal percentUsed)
        {
            Progress.Override = (budget, _) => new BudgetProgress(
                Limit: budget.Limit,
                Spent: new Money(spent, budget.Limit.Currency),
                Remaining: new Money(budget.Limit.Amount - spent, budget.Limit.Currency),
                PercentUsed: percentUsed,
                ProjectedEndOfPeriod: null,
                HasIncompleteRates: false);
            return this;
        }
    }
}
