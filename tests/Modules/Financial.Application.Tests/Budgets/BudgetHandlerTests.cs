using FluentAssertions;
using Sextante.Modules.Financial.Application.Features.Budgets;
using Sextante.Modules.Financial.Application.Tests.TestSupport;
using Sextante.Modules.Financial.Domain.Budgets;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.Budgets;

public sealed class BudgetHandlerTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private const int Year = 2026;
    private const int Month = 5;

    [Fact]
    public async Task Create_with_valid_command_persists_and_returns_response()
    {
        var category = Category.Create("Habitação", CategoryKind.Expense, "pi-home", "#0066ff", Tenant);
        var fixture = new HandlerFixture()
            .WithCategory(category);

        var command = new CreateBudgetCommand(
            CategoryId: category.Id,
            Year: Year,
            Month: Month,
            LimitAmount: 500m,
            LimitCurrency: "EUR",
            AlertThresholdPercent: 80,
            Notes: "Renda + condomínio");

        var response = await BudgetHandlers.Handle(
            command, fixture.Budgets, fixture.Categories, fixture.TenantContext,
            fixture.CurrencyDirectory, fixture.Progress, CancellationToken.None);

        response.Id.Should().NotBe(Guid.Empty);
        response.CategoryId.Should().Be(category.Id);
        response.Year.Should().Be(Year);
        response.Month.Should().Be(Month);
        response.LimitAmount.Should().Be(500m);
        response.LimitCurrency.Should().Be("EUR");
        response.AlertThresholdPercent.Should().Be(80);
        response.Notes.Should().Be("Renda + condomínio");
    }

    [Fact]
    public async Task Create_uppercases_currency_code()
    {
        var category = Category.Create("Habitação", CategoryKind.Expense, "pi-home", "#0066ff", Tenant);
        var fixture = new HandlerFixture().WithCategory(category);
        var command = NewCreateCommand(category.Id) with { LimitCurrency = "eur" };

        var response = await BudgetHandlers.Handle(
            command, fixture.Budgets, fixture.Categories, fixture.TenantContext,
            fixture.CurrencyDirectory, fixture.Progress, CancellationToken.None);

        response.LimitCurrency.Should().Be("EUR");
    }

    [Fact]
    public async Task Create_rejects_inactive_currency()
    {
        var category = Category.Create("Habitação", CategoryKind.Expense, "pi-home", "#0066ff", Tenant);
        var fixture = new HandlerFixture().WithCategory(category);
        var command = NewCreateCommand(category.Id) with { LimitCurrency = "ZZZ" };

        var act = () => BudgetHandlers.Handle(
            command, fixture.Budgets, fixture.Categories, fixture.TenantContext,
            fixture.CurrencyDirectory, fixture.Progress, CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyNotActiveException>();
    }

    [Fact]
    public async Task Create_rejects_unknown_category()
    {
        var fixture = new HandlerFixture();
        var command = NewCreateCommand(GuidV7.NewId());

        var act = () => BudgetHandlers.Handle(
            command, fixture.Budgets, fixture.Categories, fixture.TenantContext,
            fixture.CurrencyDirectory, fixture.Progress, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Create_rejects_income_category()
    {
        var category = Category.Create("Salário", CategoryKind.Income, "pi-wallet", "#00ff00", Tenant);
        var fixture = new HandlerFixture().WithCategory(category);
        var command = NewCreateCommand(category.Id);

        var act = () => BudgetHandlers.Handle(
            command, fixture.Budgets, fixture.Categories, fixture.TenantContext,
            fixture.CurrencyDirectory, fixture.Progress, CancellationToken.None);

        await act.Should().ThrowAsync<BudgetCategoryMustBeExpenseException>();
    }

    [Fact]
    public async Task Create_rejects_duplicate_category_period()
    {
        var category = Category.Create("Habitação", CategoryKind.Expense, "pi-home", "#0066ff", Tenant);
        var existing = Budget.Create(
            Tenant, category.Id, new BudgetPeriod(Year, Month),
            new Money(100m, "EUR"), 80, null);
        var fixture = new HandlerFixture()
            .WithCategory(category)
            .WithBudget(existing);

        var command = NewCreateCommand(category.Id);

        var act = () => BudgetHandlers.Handle(
            command, fixture.Budgets, fixture.Categories, fixture.TenantContext,
            fixture.CurrencyDirectory, fixture.Progress, CancellationToken.None);

        await act.Should().ThrowAsync<BudgetDuplicateForCategoryException>();
    }

    [Fact]
    public async Task Update_changes_limit_threshold_and_notes()
    {
        var category = Category.Create("Habitação", CategoryKind.Expense, "pi-home", "#0066ff", Tenant);
        var budget = Budget.Create(
            Tenant, category.Id, new BudgetPeriod(Year, Month),
            new Money(500m, "EUR"), 80, "old");
        var fixture = new HandlerFixture()
            .WithCategory(category)
            .WithBudget(budget);

        var response = await BudgetHandlers.Handle(
            new UpdateBudgetCommand(budget.Id, 700m, "EUR", 90, "novo"),
            fixture.Budgets, fixture.CurrencyDirectory, fixture.Progress,
            CancellationToken.None);

        response.Should().NotBeNull();
        response!.LimitAmount.Should().Be(700m);
        response.AlertThresholdPercent.Should().Be(90);
        response.Notes.Should().Be("novo");
    }

    [Fact]
    public async Task Update_returns_null_when_budget_not_found()
    {
        var fixture = new HandlerFixture();
        var response = await BudgetHandlers.Handle(
            new UpdateBudgetCommand(GuidV7.NewId(), 700m, "EUR", null, null),
            fixture.Budgets, fixture.CurrencyDirectory, fixture.Progress,
            CancellationToken.None);

        response.Should().BeNull();
    }

    [Fact]
    public async Task Archive_soft_deletes_budget_and_alerts()
    {
        var category = Category.Create("Habitação", CategoryKind.Expense, "pi-home", "#0066ff", Tenant);
        var budget = Budget.Create(
            Tenant, category.Id, new BudgetPeriod(Year, Month),
            new Money(500m, "EUR"), 80, null);
        var alert = BudgetAlert.Create(Tenant, budget.Id, 80, new Money(400m, "EUR"));
        var fixture = new HandlerFixture()
            .WithCategory(category)
            .WithBudget(budget)
            .WithAlert(alert);

        var ok = await BudgetHandlers.Handle(
            new ArchiveBudgetCommand(budget.Id),
            fixture.Budgets, fixture.Alerts, CancellationToken.None);

        ok.Should().BeTrue();
        budget.DeletedAt.Should().NotBeNull();
        alert.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Archive_returns_false_when_budget_not_found()
    {
        var fixture = new HandlerFixture();
        var ok = await BudgetHandlers.Handle(
            new ArchiveBudgetCommand(GuidV7.NewId()),
            fixture.Budgets, fixture.Alerts, CancellationToken.None);
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task List_filters_by_year_and_month()
    {
        var category = Category.Create("Habitação", CategoryKind.Expense, "pi-home", "#0066ff", Tenant);
        var budgetMay = Budget.Create(
            Tenant, category.Id, new BudgetPeriod(Year, Month),
            new Money(500m, "EUR"), 80, null);
        var budgetJune = Budget.Create(
            Tenant, category.Id, new BudgetPeriod(Year, 6),
            new Money(600m, "EUR"), 80, null);
        var fixture = new HandlerFixture()
            .WithCategory(category)
            .WithBudget(budgetMay)
            .WithBudget(budgetJune);

        var responses = await BudgetHandlers.Handle(
            new ListBudgetsQuery(Year, Month),
            fixture.Budgets, fixture.Progress, CancellationToken.None);

        responses.Should().HaveCount(1);
        responses[0].LimitAmount.Should().Be(500m);
    }

    [Fact]
    public async Task Acknowledge_alert_marks_acknowledged()
    {
        var budget = Budget.Create(
            Tenant, GuidV7.NewId(), new BudgetPeriod(Year, Month),
            new Money(500m, "EUR"), 80, null);
        var alert = BudgetAlert.Create(Tenant, budget.Id, 80, new Money(400m, "EUR"));
        var fixture = new HandlerFixture()
            .WithBudget(budget)
            .WithAlert(alert);

        var ok = await BudgetHandlers.Handle(
            new AcknowledgeBudgetAlertCommand(alert.Id),
            fixture.Alerts, CancellationToken.None);

        ok.Should().BeTrue();
        alert.Acknowledged.Should().BeTrue();
    }

    [Fact]
    public async Task ListActiveAlerts_includes_category_id_and_excludes_acknowledged()
    {
        var budget = Budget.Create(
            Tenant, GuidV7.NewId(), new BudgetPeriod(Year, Month),
            new Money(500m, "EUR"), 80, null);
        var active = BudgetAlert.Create(Tenant, budget.Id, 80, new Money(400m, "EUR"));
        var ack = BudgetAlert.Create(Tenant, budget.Id, 100, new Money(550m, "EUR"));
        ack.Acknowledge();
        var fixture = new HandlerFixture()
            .WithBudget(budget)
            .WithAlert(active)
            .WithAlert(ack);

        var responses = await BudgetHandlers.Handle(
            new ListActiveBudgetAlertsQuery(),
            fixture.Alerts, fixture.Budgets, CancellationToken.None);

        responses.Should().HaveCount(1);
        responses[0].Id.Should().Be(active.Id);
        responses[0].CategoryId.Should().Be(budget.CategoryId);
    }

    private static CreateBudgetCommand NewCreateCommand(Guid categoryId)
        => new(categoryId, Year, Month, 500m, "EUR", 80, null);

    private sealed class HandlerFixture
    {
        public HandlerFixture()
        {
            TenantContext = new StubTenantContext(Tenant);
            Budgets = new StubBudgetRepository();
            Alerts = new StubBudgetAlertRepository();
            Categories = new StubCategoryRepository();
            CurrencyDirectory = new StubCurrencyDirectory(new[] { "EUR", "USD", "BRL" });
            Progress = new StubBudgetProgressService();
        }

        public StubTenantContext TenantContext { get; }
        public StubBudgetRepository Budgets { get; }
        public StubBudgetAlertRepository Alerts { get; }
        public StubCategoryRepository Categories { get; }
        public StubCurrencyDirectory CurrencyDirectory { get; }
        public StubBudgetProgressService Progress { get; }

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

        public HandlerFixture WithCategory(Category category)
        {
            Categories.Store[category.Id] = category;
            return this;
        }
    }
}

internal sealed class StubTenantContext : ITenantContext
{
    public StubTenantContext(TenantId tenantId) => TenantId = tenantId;
    public TenantId TenantId { get; }
}

internal sealed class StubCurrencyDirectory : ICurrencyDirectory
{
    private readonly HashSet<string> _active;
    public StubCurrencyDirectory(IEnumerable<string> active)
        => _active = new HashSet<string>(active, StringComparer.Ordinal);
    public Task<IReadOnlyList<string>> GetActiveCodesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(_active.OrderBy(c => c).ToList());
    public Task<bool> IsActiveAsync(string code, CancellationToken ct)
        => Task.FromResult(_active.Contains(code));
}

internal sealed class StubBudgetRepository : IBudgetRepository
{
    public Dictionary<Guid, Budget> Store { get; } = new();

    public Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(Store.TryGetValue(id, out var b) && b.DeletedAt is null ? b : null);

    public Task<IReadOnlyList<Budget>> ListAsync(int year, int month, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Budget>>(Store.Values
            .Where(b => b.DeletedAt is null && b.Period.Year == year && b.Period.Month == month)
            .ToList());

    public Task<Budget?> GetForCategoryAsync(Guid categoryId, BudgetPeriod period, CancellationToken ct)
        => Task.FromResult(Store.Values
            .FirstOrDefault(b => b.DeletedAt is null
                && b.CategoryId == categoryId
                && b.Period.Year == period.Year
                && b.Period.Month == period.Month));

    public Task AddAsync(Budget budget, CancellationToken ct)
    {
        Store[budget.Id] = budget;
        return Task.CompletedTask;
    }

    public void Update(Budget budget) => Store[budget.Id] = budget;
    public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
}

internal sealed class StubBudgetAlertRepository : IBudgetAlertRepository
{
    public Dictionary<Guid, BudgetAlert> Store { get; } = new();

    public Task<BudgetAlert?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(Store.TryGetValue(id, out var a) && a.DeletedAt is null ? a : null);

    public Task<bool> ExistsAsync(Guid budgetId, int threshold, CancellationToken ct)
        => Task.FromResult(Store.Values.Any(a =>
            a.DeletedAt is null && a.BudgetId == budgetId && a.Threshold == threshold));

    public Task<IReadOnlyList<BudgetAlert>> ListActiveAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<BudgetAlert>>(Store.Values
            .Where(a => a.DeletedAt is null && !a.Acknowledged)
            .OrderByDescending(a => a.TriggeredAt)
            .ToList());

    public Task<IReadOnlyList<BudgetAlert>> ListForBudgetAsync(Guid budgetId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<BudgetAlert>>(Store.Values
            .Where(a => a.BudgetId == budgetId)
            .ToList());

    public Task AddAsync(BudgetAlert alert, CancellationToken ct)
    {
        Store[alert.Id] = alert;
        return Task.CompletedTask;
    }

    public void Update(BudgetAlert alert) => Store[alert.Id] = alert;
    public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
}

internal sealed class StubCategoryRepository : ICategoryRepository
{
    public Dictionary<Guid, Category> Store { get; } = new();

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken ct)
        => Task.FromResult(Store.TryGetValue(id, out var c) ? c : null);

    public Task<Category?> GetByIdIncludingArchivedAsync(Guid id, TenantId tenantId, CancellationToken ct)
            => GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Category>>(Store.Values.ToList());

    public Task AddAsync(Category category, CancellationToken ct)
    {
        Store[category.Id] = category;
        return Task.CompletedTask;
    }
    public void Update(Category category) => Store[category.Id] = category;
    public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
    public Task<int> CountActiveTransactionsAsync(Guid categoryId, CancellationToken ct) => Task.FromResult(0);
    public Task SeedAsync(Guid tenantId, IReadOnlyList<Category> categories, CancellationToken ct)
        => Task.CompletedTask;
}

/// <summary>
/// Devolve <see cref="BudgetProgress"/> determinístico configurável.
/// Por default: spent=0, percent=0 — para tests que não exercitam a lógica.
/// </summary>
internal sealed class StubBudgetProgressService : IBudgetProgressService
{
    public Func<Budget, DateOnly?, BudgetProgress>? Override { get; set; }

    public Task<BudgetProgress> CalculateAsync(Budget budget, DateOnly? asOfDate, CancellationToken ct)
    {
        if (Override is not null)
        {
            return Task.FromResult(Override(budget, asOfDate));
        }

        var zero = new Money(0m, budget.Limit.Currency);
        return Task.FromResult(new BudgetProgress(
            budget.Limit, zero, budget.Limit, 0m, null, false));
    }
}
