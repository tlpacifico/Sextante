using FluentAssertions;
using Sextante.Modules.Financial.Application.Features.RecurringRules;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.RecurringRules;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.RecurringRules;

public sealed class RecurringRuleHandlerTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateOnly Today = new(2026, 5, 1);
    private const string ActiveCurrency = "EUR";

    private static Account NewAccount(string currency) => Account.Create(
        name: $"Account {currency}",
        type: AccountType.Checking,
        currency: currency,
        openingBalance: new Money(0m, currency),
        tenantId: Tenant);

    private static CreateRecurringRuleCommand ValidCreateCommand() => new(
        "Renda mensal",
        Amount: 850m,
        Currency: ActiveCurrency,
        AccountId: GuidV7.NewId(),
        CategoryId: GuidV7.NewId(),
        Frequency: "Monthly",
        Interval: 1,
        StartDate: new DateOnly(2026, 6, 1),
        EndDate: null,
        Tags: new[] { "fixa", "essencial" });

    // ── CreateRecurringRule ──────────────────────────────────────────

    [Fact]
    public async Task Valid_command_creates_rule_and_returns_response()
    {
        var account = NewAccount(ActiveCurrency);
        var command = ValidCreateCommand() with { AccountId = account.Id };
        var accounts = new StubAccountRepository(account);
        var repo = new StubRecurringRuleRepository();
        var currencyDirectory = new StubCurrencyDirectory(new[] { ActiveCurrency });
        var tenantContext = new StubTenantContext(Tenant);

        var response = await RecurringRuleHandlers.Handle(
            command, repo, accounts, tenantContext, currencyDirectory,
            CancellationToken.None);

        response.Should().NotBeNull();
        response.Description.Should().Be("Renda mensal");
        response.Amount.Currency.Should().Be(ActiveCurrency);
        response.Amount.Amount.Should().Be(850m);
        response.AccountId.Should().Be(command.AccountId);
        response.CategoryId.Should().Be(command.CategoryId);
        response.Frequency.Should().Be("Monthly");
        response.Interval.Should().Be(1);
        response.StartDate.Should().Be(new DateOnly(2026, 6, 1));
        response.EndDate.Should().BeNull();
        response.IsActive.Should().BeTrue();
        response.Tags.Should().BeEquivalentTo(new[] { "fixa", "essencial" });
        response.NextOccurrence.Should().Be(new DateOnly(2026, 6, 1));

        var stored = await repo.GetByIdAsync(response.Id, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.Description.Should().Be("Renda mensal");
    }

    [Fact]
    public async Task Invalid_frequency_throws_RecurringRuleInvalidFrequencyException()
    {
        var command = ValidCreateCommand() with { Frequency = "Hourly" };
        var accounts = new StubAccountRepository();
        var repo = new StubRecurringRuleRepository();
        var currencyDirectory = new StubCurrencyDirectory(new[] { ActiveCurrency });
        var tenantContext = new StubTenantContext(Tenant);

        var act = () => RecurringRuleHandlers.Handle(
            command, repo, accounts, tenantContext, currencyDirectory,
            CancellationToken.None);

        await act.Should().ThrowAsync<RecurringRuleInvalidFrequencyException>();
    }

    [Fact]
    public async Task Inactive_currency_throws_CurrencyNotActiveException()
    {
        var command = ValidCreateCommand() with { Currency = "ZZZ" };
        var accounts = new StubAccountRepository();
        var repo = new StubRecurringRuleRepository();
        var currencyDirectory = new StubCurrencyDirectory(new[] { ActiveCurrency });
        var tenantContext = new StubTenantContext(Tenant);

        var act = () => RecurringRuleHandlers.Handle(
            command, repo, accounts, tenantContext, currencyDirectory,
            CancellationToken.None);

        await act.Should().ThrowAsync<CurrencyNotActiveException>();
    }

    [Fact]
    public async Task Account_not_found_throws_ArgumentException()
    {
        var command = ValidCreateCommand() with { AccountId = GuidV7.NewId() };
        var accounts = new StubAccountRepository(); // empty
        var repo = new StubRecurringRuleRepository();
        var currencyDirectory = new StubCurrencyDirectory(new[] { ActiveCurrency });
        var tenantContext = new StubTenantContext(Tenant);

        var act = () => RecurringRuleHandlers.Handle(
            command, repo, accounts, tenantContext, currencyDirectory,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Domain_validation_rejection_propagates()
    {
        var account = NewAccount(ActiveCurrency);
        var command = ValidCreateCommand() with
        {
            Amount = 0m,
            AccountId = account.Id
        };
        var accounts = new StubAccountRepository(account);
        var repo = new StubRecurringRuleRepository();
        var currencyDirectory = new StubCurrencyDirectory(new[] { ActiveCurrency });
        var tenantContext = new StubTenantContext(Tenant);

        var act = () => RecurringRuleHandlers.Handle(
            command, repo, accounts, tenantContext, currencyDirectory,
            CancellationToken.None);

        await act.Should().ThrowAsync<RecurringRuleAmountMustBePositiveException>();
    }

    // ── UpdateRecurringRule ──────────────────────────────────────────

    [Fact]
    public async Task Valid_update_returns_updated_rule()
    {
        var account = NewAccount(ActiveCurrency);
        var existing = RecurringRule.Create(
            "Original", new Money(100m, ActiveCurrency), account.Id, null,
            Frequency.Monthly, 1, new DateOnly(2026, 6, 1), null,
            new[] { "tag1" }, Tenant);
        var accounts = new StubAccountRepository(account);
        var repo = new StubRecurringRuleRepository(existing);
        var currencyDirectory = new StubCurrencyDirectory(new[] { ActiveCurrency });

        var command = new UpdateRecurringRuleCommand(
            existing.Id,
            "Updated description",
            Amount: 200m,
            Currency: ActiveCurrency,
            AccountId: account.Id,
            CategoryId: GuidV7.NewId(),
            Frequency: "Weekly",
            Interval: 2,
            StartDate: new DateOnly(2026, 7, 1),
            EndDate: new DateOnly(2026, 12, 31),
            IsActive: false,
            Tags: new[] { "nova" });

        var response = await RecurringRuleHandlers.Handle(
            command, repo, accounts, currencyDirectory, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Description.Should().Be("Updated description");
        response.Amount.Amount.Should().Be(200m);
        response.Frequency.Should().Be("Weekly");
        response.Interval.Should().Be(2);
        response.StartDate.Should().Be(new DateOnly(2026, 7, 1));
        response.EndDate.Should().Be(new DateOnly(2026, 12, 31));
        response.IsActive.Should().BeFalse();
        response.Tags.Should().BeEquivalentTo(new[] { "nova" });
    }

    [Fact]
    public async Task Update_rule_not_found_returns_null()
    {
        var accounts = new StubAccountRepository();
        var repo = new StubRecurringRuleRepository();
        var currencyDirectory = new StubCurrencyDirectory(new[] { ActiveCurrency });

        var command = new UpdateRecurringRuleCommand(
            GuidV7.NewId(),
            "Does not matter",
            Amount: 100m,
            Currency: ActiveCurrency,
            AccountId: GuidV7.NewId(),
            CategoryId: null,
            Frequency: "Monthly",
            Interval: 1,
            StartDate: Today,
            EndDate: null,
            IsActive: true,
            Tags: null);

        var response = await RecurringRuleHandlers.Handle(
            command, repo, accounts, currencyDirectory, CancellationToken.None);

        response.Should().BeNull();
    }

    [Fact]
    public async Task Update_invalid_frequency_throws()
    {
        var account = NewAccount(ActiveCurrency);
        var existing = RecurringRule.Create(
            "Original", new Money(100m, ActiveCurrency), account.Id, null,
            Frequency.Monthly, 1, new DateOnly(2026, 6, 1), null, null, Tenant);
        var accounts = new StubAccountRepository(account);
        var repo = new StubRecurringRuleRepository(existing);
        var currencyDirectory = new StubCurrencyDirectory(new[] { ActiveCurrency });

        var command = new UpdateRecurringRuleCommand(
            existing.Id,
            "X", 100m, ActiveCurrency, account.Id, null,
            Frequency: "Biannual",
            Interval: 1, StartDate: Today, EndDate: null, IsActive: true, Tags: null);

        var act = () => RecurringRuleHandlers.Handle(
            command, repo, accounts, currencyDirectory, CancellationToken.None);

        await act.Should().ThrowAsync<RecurringRuleInvalidFrequencyException>();
    }

    // ── ArchiveRecurringRule ─────────────────────────────────────────

    [Fact]
    public async Task Existing_rule_archives_and_returns_true()
    {
        var rule = RecurringRule.Create(
            "To archive", new Money(50m, ActiveCurrency), GuidV7.NewId(), null,
            Frequency.Monthly, 1, new DateOnly(2026, 6, 1), null, null, Tenant);
        var repo = new StubRecurringRuleRepository(rule);

        var result = await RecurringRuleHandlers.Handle(
            new ArchiveRecurringRuleCommand(rule.Id), repo, CancellationToken.None);

        result.Should().BeTrue();
        rule.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Non_existing_rule_returns_false()
    {
        var repo = new StubRecurringRuleRepository();

        var result = await RecurringRuleHandlers.Handle(
            new ArchiveRecurringRuleCommand(GuidV7.NewId()), repo, CancellationToken.None);

        result.Should().BeFalse();
    }

    // ── GetRecurringRuleById ─────────────────────────────────────────

    [Fact]
    public async Task Existing_rule_returns_response()
    {
        var rule = RecurringRule.Create(
            "Lookup rule", new Money(75m, ActiveCurrency), GuidV7.NewId(),
            GuidV7.NewId(), Frequency.Weekly, 1, new DateOnly(2026, 6, 1),
            null, new[] { "a", "b" }, Tenant);
        var repo = new StubRecurringRuleRepository(rule);

        var response = await RecurringRuleHandlers.Handle(
            new GetRecurringRuleByIdQuery(rule.Id), repo, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Description.Should().Be("Lookup rule");
        response.Amount.Amount.Should().Be(75m);
        response.CategoryId.Should().Be(rule.CategoryId);
        response.Frequency.Should().Be("Weekly");
        response.Tags.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public async Task Non_existing_rule_returns_null()
    {
        var repo = new StubRecurringRuleRepository();

        var response = await RecurringRuleHandlers.Handle(
            new GetRecurringRuleByIdQuery(GuidV7.NewId()), repo, CancellationToken.None);

        response.Should().BeNull();
    }

    // ── ListRecurringRules ───────────────────────────────────────────

    [Fact]
    public async Task Returns_mapped_list()
    {
        var rule1 = RecurringRule.Create(
            "Rule A", new Money(10m, ActiveCurrency), GuidV7.NewId(), null,
            Frequency.Daily, 1, Today, null, null, Tenant);
        var rule2 = RecurringRule.Create(
            "Rule B", new Money(20m, ActiveCurrency), GuidV7.NewId(), null,
            Frequency.Weekly, 2, Today, null, null, Tenant);
        var repo = new StubRecurringRuleRepository(rule1, rule2);

        var response = await RecurringRuleHandlers.Handle(
            new ListRecurringRulesQuery(), repo, CancellationToken.None);

        response.Should().HaveCount(2);
        response.Should().ContainSingle(r => r.Description == "Rule A");
        response.Should().ContainSingle(r => r.Description == "Rule B");
    }

    [Fact]
    public async Task List_returns_empty_when_no_rules()
    {
        var repo = new StubRecurringRuleRepository();

        var response = await RecurringRuleHandlers.Handle(
            new ListRecurringRulesQuery(), repo, CancellationToken.None);

        response.Should().BeEmpty();
    }

    // ── GetUpcomingOccurrences ───────────────────────────────────────

    [Fact]
    public async Task Returns_dates_for_existing_rule()
    {
        var rule = RecurringRule.Create(
            "Daily rule", new Money(10m, ActiveCurrency), GuidV7.NewId(), null,
            Frequency.Daily, 1, Today, null, null, Tenant, today: Today);
        var repo = new StubRecurringRuleRepository(rule);

        var response = await RecurringRuleHandlers.Handle(
            new GetUpcomingOccurrencesQuery(rule.Id, Count: 5), repo,
            CancellationToken.None);

        response.Should().HaveCount(5);
        response[0].Should().Be(Today);
        response[1].Should().Be(Today.AddDays(1));
        response[2].Should().Be(Today.AddDays(2));
        response[3].Should().Be(Today.AddDays(3));
        response[4].Should().Be(Today.AddDays(4));
    }

    [Fact]
    public async Task Occurrences_returns_empty_when_rule_not_found()
    {
        var repo = new StubRecurringRuleRepository();

        var response = await RecurringRuleHandlers.Handle(
            new GetUpcomingOccurrencesQuery(GuidV7.NewId(), Count: 5), repo,
            CancellationToken.None);

        response.Should().BeEmpty();
    }

    [Fact]
    public async Task Clamps_count_to_max_50()
    {
        var rule = RecurringRule.Create(
            "Daily rule", new Money(10m, ActiveCurrency), GuidV7.NewId(), null,
            Frequency.Daily, 1, Today, null, null, Tenant, today: Today);
        var repo = new StubRecurringRuleRepository(rule);

        var response = await RecurringRuleHandlers.Handle(
            new GetUpcomingOccurrencesQuery(rule.Id, Count: 100), repo,
            CancellationToken.None);

        response.Should().HaveCount(50);
    }

    // ── Stubs ────────────────────────────────────────────────────────

    private sealed class StubRecurringRuleRepository : IRecurringRuleRepository
    {
        private readonly Dictionary<Guid, RecurringRule> _rules = new();

        public StubRecurringRuleRepository() { }

        public StubRecurringRuleRepository(params RecurringRule[] rules)
        {
            foreach (var rule in rules) _rules[rule.Id] = rule;
        }

        public Task<RecurringRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_rules.TryGetValue(id, out var rule) ? rule : null);

        public Task<List<RecurringRule>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(_rules.Values.ToList());

        public Task AddAsync(RecurringRule rule, CancellationToken cancellationToken)
        {
            _rules[rule.Id] = rule;
            return Task.CompletedTask;
        }

        public void Update(RecurringRule rule) => _rules[rule.Id] = rule;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<List<RecurringRule>> GetActiveRulesDueAsync(DateOnly runDate, CancellationToken cancellationToken)
            => Task.FromResult(_rules.Values
                .Where(r => r.IsActive && r.DeletedAt == null && r.NextOccurrence <= runDate)
                .ToList());

        public Task<bool> HasMaterializedAsync(Guid ruleId, DateOnly occurrenceDate, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class StubAccountRepository : IAccountRepository
    {
        private readonly Dictionary<Guid, Account> _accounts;

        public StubAccountRepository(params Account[] accounts)
            => _accounts = accounts.ToDictionary(a => a.Id);

        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => Task.FromResult(_accounts.TryGetValue(id, out var a) ? a : null);

        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Account>>(_accounts.Values.ToList());

        public Task AddAsync(Account account, CancellationToken cancellationToken)
        {
            _accounts[account.Id] = account;
            return Task.CompletedTask;
        }

        public void Update(Account account) => _accounts[account.Id] = account;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> CountActiveTransactionsAsync(Guid accountId, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(TenantId tenantId) => TenantId = tenantId;
        public TenantId TenantId { get; }
    }

    private sealed class StubCurrencyDirectory : ICurrencyDirectory
    {
        private readonly HashSet<string> _active;

        public StubCurrencyDirectory(IEnumerable<string> active)
            => _active = new HashSet<string>(active, StringComparer.Ordinal);

        public Task<IReadOnlyList<string>> GetActiveCodesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(_active.OrderBy(c => c).ToList());

        public Task<bool> IsActiveAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(_active.Contains(code));
    }
}
