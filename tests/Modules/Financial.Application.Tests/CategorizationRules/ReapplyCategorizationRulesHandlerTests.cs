using FluentAssertions;
using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.Features.CategorizationRules;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.CategorizationRules;

public sealed class ReapplyCategorizationRulesHandlerTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private static Transaction NewTx(
        Guid accountId, Guid categoryId, DateTimeOffset occurredAt,
        decimal amount, string description)
    {
        return Transaction.Create(
            accountId, categoryId, occurredAt,
            new Money(amount, "EUR"), description, null, Tenant,
            now: DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task OnlyUncategorized_skips_transactions_already_categorized()
    {
        var account = Guid.NewGuid();
        var oldCategory = Guid.NewGuid();
        var newCategory = Guid.NewGuid();

        var alreadyCategorized = NewTx(account, oldCategory, DateTimeOffset.UtcNow.AddDays(-1), 10m, "Continente");
        alreadyCategorized.MarkCategorizedByRule(Guid.NewGuid());

        var uncategorized = NewTx(account, oldCategory, DateTimeOffset.UtcNow.AddDays(-1), 20m, "Continente");

        var txRepo = new StubTxRepo(alreadyCategorized, uncategorized);
        var engine = new StubEngine(matchToCategory: newCategory, matchedRule: Guid.NewGuid());

        var response = await CategorizationRuleHandlers.Handle(
            new ReapplyCategorizationRulesCommand(null, null, null, OnlyUncategorized: true),
            txRepo, engine, new StubTenantContext(Tenant), CancellationToken.None);

        response.TotalProcessed.Should().Be(1, "the already-categorized one is filtered out");
        response.CategorizedCount.Should().Be(1);
        alreadyCategorized.CategoryId.Should().Be(oldCategory);
        uncategorized.CategoryId.Should().Be(newCategory);
    }

    [Fact]
    public async Task Without_OnlyUncategorized_re_categorizes_all_transactions_with_descriptions()
    {
        var account = Guid.NewGuid();
        var oldCategory = Guid.NewGuid();
        var newCategory = Guid.NewGuid();

        var t1 = NewTx(account, oldCategory, DateTimeOffset.UtcNow.AddDays(-1), 10m, "Continente");
        t1.MarkCategorizedByRule(Guid.NewGuid());
        var t2 = NewTx(account, oldCategory, DateTimeOffset.UtcNow.AddDays(-1), 20m, "Continente");

        var txRepo = new StubTxRepo(t1, t2);
        var engine = new StubEngine(matchToCategory: newCategory, matchedRule: Guid.NewGuid());

        var response = await CategorizationRuleHandlers.Handle(
            new ReapplyCategorizationRulesCommand(null, null, null, OnlyUncategorized: false),
            txRepo, engine, new StubTenantContext(Tenant), CancellationToken.None);

        response.TotalProcessed.Should().Be(2);
        response.CategorizedCount.Should().Be(2);
        t1.CategoryId.Should().Be(newCategory);
        t2.CategoryId.Should().Be(newCategory);
    }

    [Fact]
    public async Task Skips_transactions_with_blank_description()
    {
        var account = Guid.NewGuid();
        var category = Guid.NewGuid();

        var withDescription = NewTx(account, category, DateTimeOffset.UtcNow.AddDays(-1), 10m, "Continente");
        var blankDescription = Transaction.Create(
            account, category, DateTimeOffset.UtcNow.AddDays(-1),
            new Money(10m, "EUR"), null, null, Tenant);

        var txRepo = new StubTxRepo(withDescription, blankDescription);
        var engine = new StubEngine(matchToCategory: Guid.NewGuid(), matchedRule: Guid.NewGuid());

        var response = await CategorizationRuleHandlers.Handle(
            new ReapplyCategorizationRulesCommand(null, null, null, OnlyUncategorized: false),
            txRepo, engine, new StubTenantContext(Tenant), CancellationToken.None);

        response.TotalProcessed.Should().Be(1);
        engine.LastInputs.Should().HaveCount(1);
    }

    [Fact]
    public async Task Filter_arguments_are_forwarded_to_repository()
    {
        var category = Guid.NewGuid();
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow.AddDays(-1);

        var txRepo = new StubTxRepo();
        var engine = new StubEngine(matchToCategory: null, matchedRule: null);

        await CategorizationRuleHandlers.Handle(
            new ReapplyCategorizationRulesCommand(category, from, to, OnlyUncategorized: false),
            txRepo, engine, new StubTenantContext(Tenant), CancellationToken.None);

        txRepo.LastFilter.Should().NotBeNull();
        txRepo.LastFilter!.DateFrom.Should().Be(from);
        txRepo.LastFilter.DateTo.Should().Be(to);
        txRepo.LastFilter.CategoryIds.Should().BeEquivalentTo(new[] { category });
    }

    [Fact]
    public async Task When_no_rule_matches_unchanged_count_equals_total_processed()
    {
        var t1 = NewTx(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), 10m, "Continente");
        var t2 = NewTx(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), 20m, "Pingo Doce");

        var txRepo = new StubTxRepo(t1, t2);
        var engine = new StubEngine(matchToCategory: null, matchedRule: null);

        var response = await CategorizationRuleHandlers.Handle(
            new ReapplyCategorizationRulesCommand(null, null, null, OnlyUncategorized: false),
            txRepo, engine, new StubTenantContext(Tenant), CancellationToken.None);

        response.TotalProcessed.Should().Be(2);
        response.CategorizedCount.Should().Be(0);
        response.UnchangedCount.Should().Be(2);
    }

    [Fact]
    public async Task Skips_when_rule_match_resolves_to_same_category()
    {
        var category = Guid.NewGuid();
        var tx = NewTx(Guid.NewGuid(), category, DateTimeOffset.UtcNow.AddDays(-1), 10m, "Continente");
        var txRepo = new StubTxRepo(tx);
        // Engine reports the same category the tx already has — handler must not count as re-categorized.
        var engine = new StubEngine(matchToCategory: category, matchedRule: Guid.NewGuid());

        var response = await CategorizationRuleHandlers.Handle(
            new ReapplyCategorizationRulesCommand(null, null, null, OnlyUncategorized: false),
            txRepo, engine, new StubTenantContext(Tenant), CancellationToken.None);

        response.CategorizedCount.Should().Be(0);
        response.UnchangedCount.Should().Be(1);
    }

    private sealed class StubTxRepo : ITransactionRepository
    {
        public List<Transaction> Items { get; }
        public TransactionFilter? LastFilter { get; private set; }

        public StubTxRepo(params Transaction[] items) => Items = items.ToList();

        public Task<TransactionPage> ListAsync(TransactionFilter filter, CancellationToken ct)
        {
            LastFilter = filter;
            IEnumerable<Transaction> filtered = Items;
            if (filter.DateFrom.HasValue) filtered = filtered.Where(t => t.OccurredAt >= filter.DateFrom.Value);
            if (filter.DateTo.HasValue) filtered = filtered.Where(t => t.OccurredAt <= filter.DateTo.Value);
            if (filter.CategoryIds is { Count: > 0 }) filtered = filtered.Where(t => filter.CategoryIds.Contains(t.CategoryId));
            return Task.FromResult(new TransactionPage(filtered.ToList(), null));
        }

        public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<Transaction?>(Items.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Transaction t, CancellationToken ct) { Items.Add(t); return Task.CompletedTask; }
        public void Update(Transaction t) { /* tracked in-place via shared instances */ }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<TransactionTotals> GetConvertedTotalsAsync(TransactionFilter filter, string primaryCurrency, CancellationToken ct)
            => Task.FromResult(new TransactionTotals(new Money(0m, primaryCurrency), new Money(0m, primaryCurrency), new Money(0m, primaryCurrency)));
        public Task<IReadOnlyList<TransactionTotalsByCurrencyRow>> GetTotalsByCurrencyAsync(TransactionFilter filter, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TransactionTotalsByCurrencyRow>>(Array.Empty<TransactionTotalsByCurrencyRow>());
        public Task<IReadOnlyList<TransactionByCategoryRow>> GetByCategoryAsync(TransactionFilter filter, CategoryKindFilter kindFilter, string primaryCurrency, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TransactionByCategoryRow>>(Array.Empty<TransactionByCategoryRow>());
    }

    private sealed class StubEngine : ICategorizationRuleEngine
    {
        private readonly Guid? _matchToCategory;
        private readonly Guid? _matchedRule;
        public IReadOnlyList<TransactionToCategorize> LastInputs { get; private set; } = Array.Empty<TransactionToCategorize>();

        public StubEngine(Guid? matchToCategory, Guid? matchedRule)
        {
            _matchToCategory = matchToCategory;
            _matchedRule = matchedRule;
        }

        public Task<IReadOnlyList<CategorizationMatchResult>> ApplyAsync(
            IReadOnlyList<TransactionToCategorize> transactions, TenantId tenantId, CancellationToken ct)
        {
            LastInputs = transactions;
            var results = transactions
                .Select(t => new CategorizationMatchResult(t.TransactionId, _matchedRule, _matchToCategory))
                .ToList();
            return Task.FromResult<IReadOnlyList<CategorizationMatchResult>>(results);
        }
    }

    private sealed class StubTenantContext : ITenantContext
    {
        public StubTenantContext(TenantId tenantId) => TenantId = tenantId;
        public TenantId TenantId { get; }
    }
}
