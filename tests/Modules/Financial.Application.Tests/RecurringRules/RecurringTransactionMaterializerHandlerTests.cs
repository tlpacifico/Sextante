using FluentAssertions;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Application.Features.RecurringRules.Materialization;
using Sextante.Modules.Financial.Application.Tests.TestSupport;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.RecurringRules;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sextante.Modules.Financial.Application.Tests.RecurringRules;

public sealed class RecurringTransactionMaterializerHandlerTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Guid AccountId = GuidV7.NewId();
    private static readonly Guid CategoryId = GuidV7.NewId();
    private static readonly DateOnly RunDate = new(2026, 5, 1);

    [Fact]
    public async Task No_rules_due_returns_zero_results()
    {
        var fixture = new HandlerFixture(new DateOnly(2026, 5, 1));

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.TotalRulesProcessed.Should().Be(0);
        result.Materialized.Should().Be(0);
        result.SkippedDuplicate.Should().Be(0);
        result.SkippedNoRate.Should().Be(0);
        result.Completed.Should().Be(0);
    }

    [Fact]
    public async Task Same_currency_rule_materializes_transaction_without_exchange_rate()
    {
        var rule = NewRule(today: RunDate);
        var fixture = new HandlerFixture(RunDate, primaryCurrency: "EUR")
            .WithRule(rule);

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.Materialized.Should().Be(1);
        result.TotalRulesProcessed.Should().Be(1);

        var tx = fixture.Transactions.Added.Should().ContainSingle().Subject;
        tx.AccountId.Should().Be(rule.AccountId);
        tx.CategoryId.Should().Be(rule.CategoryId!.Value);
        tx.Amount.Should().Be(rule.Amount);
        tx.ExchangeRateToPrimary.Should().BeNull();
        tx.ExchangeRateAt.Should().BeNull();
    }

    [Fact]
    public async Task Different_currency_rule_resolves_exchange_rate_and_materializes()
    {
        var rateAt = DateTimeOffset.UtcNow;
        var snapshot = new ExchangeRateSnapshot(0.85m, rateAt);
        var rule = NewRule(currency: "USD", today: RunDate);
        var fixture = new HandlerFixture(RunDate, primaryCurrency: "EUR")
            .WithRule(rule)
            .WithExchangeRate(snapshot);

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.Materialized.Should().Be(1);

        var tx = fixture.Transactions.Added.Should().ContainSingle().Subject;
        tx.ExchangeRateToPrimary.Should().Be(0.85m);
        tx.ExchangeRateAt.Should().Be(rateAt);
        tx.Amount.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Exchange_rate_unavailable_skips_occurrence_counts_skippedNoRate()
    {
        var rule = NewRule(currency: "JPY", today: RunDate);
        var fixture = new HandlerFixture(RunDate, primaryCurrency: "EUR")
            .WithRule(rule)
            .WithExchangeRateThrows("JPY", "EUR", RunDate);

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.SkippedNoRate.Should().Be(1);
        result.Materialized.Should().Be(0);
        fixture.Transactions.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Already_materialized_occurrence_is_skipped_counts_skippedDuplicate()
    {
        var rule = NewRule(today: RunDate);
        var fixture = new HandlerFixture(RunDate, primaryCurrency: "EUR")
            .WithRule(rule)
            .WithAlreadyMaterialized(rule.Id, RunDate);

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.SkippedDuplicate.Should().Be(1);
        result.Materialized.Should().Be(0);
        fixture.Transactions.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Rule_completes_when_advancing_past_end_date_counts_completed()
    {
        var rule = RecurringRule.Create(
            "Completes today", new Money(100m, "EUR"), AccountId, CategoryId,
            Frequency.Daily, 1,
            RunDate, RunDate, null, Tenant, RunDate);
        var fixture = new HandlerFixture(RunDate, primaryCurrency: "EUR")
            .WithRule(rule);

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.Materialized.Should().Be(1);
        result.Completed.Should().Be(1);
        rule.NextOccurrence.Should().BeNull();
    }

    [Fact]
    public async Task Multiple_rules_mixed_results_sums_counts_correctly()
    {
        var rule1 = NewRule(today: RunDate);
        var rule2 = NewRule(currency: "JPY", today: RunDate);
        var rule3 = RecurringRule.Create(
            "Completes", new Money(50m, "EUR"), AccountId, CategoryId,
            Frequency.Daily, 1,
            RunDate, RunDate, null, Tenant, RunDate);

        var fixture = new HandlerFixture(RunDate, primaryCurrency: "EUR")
            .WithRules(new[] { rule1, rule2, rule3 })
            .WithExchangeRateThrows("JPY", "EUR", RunDate);

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.TotalRulesProcessed.Should().Be(3);
        result.Materialized.Should().Be(2);
        result.SkippedNoRate.Should().Be(1);
        result.Completed.Should().Be(1);
    }

    [Fact]
    public async Task Rule_without_category_uses_Guid_Empty()
    {
        var rule = RecurringRule.Create(
            "Uncategorized", new Money(75m, "EUR"), AccountId, null,
            Frequency.Monthly, 1,
            RunDate, null, null, Tenant, RunDate);
        var fixture = new HandlerFixture(RunDate, primaryCurrency: "EUR")
            .WithRule(rule);

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.Materialized.Should().Be(1);
        var tx = fixture.Transactions.Added.Should().ContainSingle().Subject;
        tx.CategoryId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task Rule_with_tags_copies_tags_to_transaction()
    {
        var tagRule = RecurringRule.Create(
            "Tagged rule", new Money(80m, "EUR"), AccountId, CategoryId,
            Frequency.Monthly, 1,
            RunDate, null, new[] { "fix", "monthly" }, Tenant, RunDate);
        var fixture = new HandlerFixture(RunDate, primaryCurrency: "EUR")
            .WithRule(tagRule);

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.Materialized.Should().Be(1);
        var tx = fixture.Transactions.Added.Should().ContainSingle().Subject;
        tx.Tags.Should().BeEquivalentTo(new[] { "fix", "monthly" });
    }

    [Fact]
    public async Task Materializer_uses_Math_Abs_for_amount()
    {
        // Rules are normally created with positive amounts, but the
        // materializer uses Math.Abs defensively via DetermineSignedAmount.
        // We simulate by creating a rule and verifying the Transaction.Amount
        // equals Math.Abs(rule.Amount.Amount).
        var rule = NewRule(today: RunDate);
        var fixture = new HandlerFixture(RunDate, primaryCurrency: "EUR")
            .WithRule(rule);

        var result = await fixture.Sut.ExecuteAsync(
            new RecurringMaterializerPayload(RunDate),
            fixture.TenantContext,
            CancellationToken.None);

        result.Materialized.Should().Be(1);
        var tx = fixture.Transactions.Added.Should().ContainSingle().Subject;
        tx.Amount.Amount.Should().Be(Math.Abs(rule.Amount.Amount));
    }

    private static RecurringRule NewRule(
        string currency = "EUR",
        DateOnly? today = null)
    {
        var ruleToday = today ?? RunDate;
        return RecurringRule.Create(
            "Test rule", new Money(100m, currency), AccountId, CategoryId,
            Frequency.Monthly, 1,
            ruleToday, null, null, Tenant, ruleToday);
    }

    private sealed class HandlerFixture
    {
        public HandlerFixture(
            DateOnly runDate,
            string primaryCurrency = "EUR")
        {
            TenantContext = new StubTenantContext(Tenant);
            TenantCurrency = new StubTenantCurrencyResolver(primaryCurrency);
            Transactions = new InMemoryTransactionRepository();
            Rules = new StubRecurringRuleRepository();
            ExchangeRateService = new StubExchangeRateService(returnsNull: true);
            Categories = new StubCategoryRepository();
            Events = new StubIntegrationEventPublisher();
        }

        public StubTenantContext TenantContext { get; }
        public StubTenantCurrencyResolver TenantCurrency { get; }
        public InMemoryTransactionRepository Transactions { get; }
        public StubRecurringRuleRepository Rules { get; }
        public StubExchangeRateService ExchangeRateService { get; private set; }
        public StubCategoryRepository Categories { get; }
        public StubIntegrationEventPublisher Events { get; }

        public RecurringTransactionMaterializerHandler Sut =>
            new(
                Rules,
                Transactions,
                Categories,
                ExchangeRateService,
                TenantCurrency,
                Events,
                NullLoggerFactory.Instance.CreateLogger<RecurringTransactionMaterializerHandler>());

        public HandlerFixture WithRule(RecurringRule rule)
        {
            Rules.FromGetActive.Add(rule);
            return this;
        }

        public HandlerFixture WithRules(IEnumerable<RecurringRule> rules)
        {
            Rules.FromGetActive.AddRange(rules);
            return this;
        }

        public HandlerFixture WithExchangeRate(ExchangeRateSnapshot snapshot)
        {
            ExchangeRateService = new StubExchangeRateService(returnsNull: false, snapshot: snapshot);
            return this;
        }

        public HandlerFixture WithExchangeRateThrows(string from, string to, DateOnly date)
        {
            ExchangeRateService = new StubExchangeRateService(throws: new ExchangeRateUnavailableException(from, to, date));
            return this;
        }

        public HandlerFixture WithAlreadyMaterialized(Guid ruleId, DateOnly occurrenceDate)
        {
            Rules.Materialized.Add((ruleId, occurrenceDate));
            return this;
        }
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(TenantId tenantId) => TenantId = tenantId;
        public TenantId TenantId { get; }
    }

    private sealed class StubTenantCurrencyResolver : ITenantCurrencyResolver
    {
        private readonly string _primary;
        public StubTenantCurrencyResolver(string primary) => _primary = primary;
        public Task<string> GetPrimaryCurrencyAsync(CancellationToken ct)
            => Task.FromResult(_primary);
    }

    private sealed class StubRecurringRuleRepository : IRecurringRuleRepository
    {
        public List<RecurringRule> FromGetActive { get; } = new();
        public List<(Guid RuleId, DateOnly OccurrenceDate)> Materialized { get; } = new();
        public List<RecurringRule> Added { get; } = new();

        public Task<RecurringRule?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<RecurringRule?>(FromGetActive.FirstOrDefault(r => r.Id == id));

        public Task<List<RecurringRule>> ListAsync(CancellationToken ct)
            => Task.FromResult(FromGetActive.ToList());

        public Task AddAsync(RecurringRule rule, CancellationToken ct)
        {
            Added.Add(rule);
            return Task.CompletedTask;
        }

        public void Update(RecurringRule rule) { /* no-op */ }

        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);

        public Task<List<RecurringRule>> GetActiveRulesDueAsync(DateOnly runDate, CancellationToken ct)
            => Task.FromResult(FromGetActive.ToList());

        public Task<bool> HasMaterializedAsync(Guid ruleId, DateOnly occurrenceDate, CancellationToken ct)
        {
            var exists = Materialized.Any(m =>
                m.RuleId == ruleId && m.OccurrenceDate == occurrenceDate);
            return Task.FromResult(exists);
        }
    }

    private sealed class InMemoryTransactionRepository : ITransactionRepository
    {
        public List<Transaction> Added { get; } = new();

        public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Transaction?>(Added.FirstOrDefault(t => t.Id == id));

        public Task AddAsync(Transaction transaction, CancellationToken ct)
        {
            Added.Add(transaction);
            return Task.CompletedTask;
        }

        public void Update(Transaction transaction) { /* no-op */ }

        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);

        public Task<TransactionPage> ListAsync(TransactionFilter filter, CancellationToken ct)
            => Task.FromResult(new TransactionPage(Added, null));

        public Task<TransactionTotals> GetConvertedTotalsAsync(
            TransactionFilter filter, string primaryCurrency, CancellationToken ct)
            => Task.FromResult(new TransactionTotals(
                new Money(0m, primaryCurrency),
                new Money(0m, primaryCurrency),
                new Money(0m, primaryCurrency)));

        public Task<IReadOnlyList<TransactionTotalsByCurrencyRow>> GetTotalsByCurrencyAsync(
            TransactionFilter filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TransactionTotalsByCurrencyRow>>(
                Array.Empty<TransactionTotalsByCurrencyRow>());

        public Task<IReadOnlyList<TransactionByCategoryRow>> GetByCategoryAsync(
            TransactionFilter filter, CategoryKindFilter kindFilter, string primaryCurrency, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TransactionByCategoryRow>>(
                Array.Empty<TransactionByCategoryRow>());
    }

    private sealed class StubExchangeRateService : IExchangeRateService
    {
        private readonly bool _returnsNull;
        private readonly ExchangeRateSnapshot? _snapshot;
        private readonly Exception? _throws;

        public StubExchangeRateService(
            bool returnsNull = true,
            ExchangeRateSnapshot? snapshot = null,
            Exception? throws = null)
        {
            _returnsNull = returnsNull;
            _snapshot = snapshot;
            _throws = throws;
        }

        public Task<ExchangeRateSnapshot?> ResolveAsync(
            string fromCurrency, string toCurrency, DateTimeOffset at, CancellationToken ct)
        {
            if (_throws is not null)
            {
                throw _throws;
            }

            return Task.FromResult(_returnsNull ? null : _snapshot);
        }
    }

    private sealed class StubCategoryRepository : ICategoryRepository
    {
        public Task<Category?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Category?>(null);

        public Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Category>>(Array.Empty<Category>());

        public Task AddAsync(Category category, CancellationToken ct)
            => Task.CompletedTask;

        public void Update(Category category) { /* no-op */ }

        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);

        public Task<int> CountActiveTransactionsAsync(Guid categoryId, CancellationToken ct)
            => Task.FromResult(0);

        public Task SeedAsync(Guid tenantId, IReadOnlyList<Category> categories, CancellationToken ct)
            => Task.CompletedTask;
    }
}
