using FluentAssertions;
using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Domain.CategorizationRules;
using Sextante.Modules.Financial.Infrastructure.CategorizationRules;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.CategorizationRules;

public sealed class CategorizationRuleEngineTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private static CategorizationRule Rule(
        string name, string pattern, CategorizationMatchType matchType,
        Guid categoryId, int priority, bool active = true)
    {
        var r = CategorizationRule.Create(name, pattern, matchType, categoryId, priority, Tenant);
        if (!active)
            r.Update(name, pattern, matchType, categoryId, priority, isActive: false);
        return r;
    }

    [Fact]
    public async Task Contains_match_is_case_insensitive()
    {
        var category = Guid.NewGuid();
        var repo = new StubRuleRepo(Rule("Continente", "Continente", CategorizationMatchType.Contains, category, 0));
        var engine = new CategorizationRuleEngine(repo);

        var results = await engine.ApplyAsync(
            new[] { new TransactionToCategorize(Guid.NewGuid(), "Supermercado CONTINENTE Lisboa") },
            Tenant, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].NewCategoryId.Should().Be(category);
    }

    [Fact]
    public async Task Equals_match_is_case_insensitive()
    {
        var category = Guid.NewGuid();
        var repo = new StubRuleRepo(Rule("Cont", "continente", CategorizationMatchType.Equals, category, 0));
        var engine = new CategorizationRuleEngine(repo);

        var results = await engine.ApplyAsync(
            new[] { new TransactionToCategorize(Guid.NewGuid(), "CONTINENTE") },
            Tenant, CancellationToken.None);

        results[0].NewCategoryId.Should().Be(category);
    }

    [Fact]
    public async Task Equals_does_not_match_when_description_has_extra_text()
    {
        var category = Guid.NewGuid();
        var repo = new StubRuleRepo(Rule("Cont", "continente", CategorizationMatchType.Equals, category, 0));
        var engine = new CategorizationRuleEngine(repo);

        var results = await engine.ApplyAsync(
            new[] { new TransactionToCategorize(Guid.NewGuid(), "CONTINENTE Lisboa") },
            Tenant, CancellationToken.None);

        results[0].NewCategoryId.Should().BeNull();
        results[0].MatchedRuleId.Should().BeNull();
    }

    [Fact]
    public async Task StartsWith_match_is_case_insensitive()
    {
        var category = Guid.NewGuid();
        var repo = new StubRuleRepo(Rule("Pag", "PAGAMENTO", CategorizationMatchType.StartsWith, category, 0));
        var engine = new CategorizationRuleEngine(repo);

        var results = await engine.ApplyAsync(
            new[] { new TransactionToCategorize(Guid.NewGuid(), "pagamento serviço água") },
            Tenant, CancellationToken.None);

        results[0].NewCategoryId.Should().Be(category);
    }

    [Fact]
    public async Task First_match_wins_by_priority_order()
    {
        var lowPriorityCategory = Guid.NewGuid();
        var highPriorityCategory = Guid.NewGuid();

        // Repo returns rules in priority order (Priority 5 < Priority 10).
        var repo = new StubRuleRepo(
            Rule("HighPrio", "Continente", CategorizationMatchType.Contains, highPriorityCategory, priority: 5),
            Rule("LowPrio", "Continente", CategorizationMatchType.Contains, lowPriorityCategory, priority: 10));
        var engine = new CategorizationRuleEngine(repo);

        var results = await engine.ApplyAsync(
            new[] { new TransactionToCategorize(Guid.NewGuid(), "Continente Lisboa") },
            Tenant, CancellationToken.None);

        results[0].NewCategoryId.Should().Be(highPriorityCategory);
    }

    [Fact]
    public async Task No_match_returns_null_category_and_rule()
    {
        var repo = new StubRuleRepo(
            Rule("R1", "ContinenteOnly", CategorizationMatchType.Contains, Guid.NewGuid(), 0));
        var engine = new CategorizationRuleEngine(repo);

        var results = await engine.ApplyAsync(
            new[] { new TransactionToCategorize(Guid.NewGuid(), "Pingo Doce") },
            Tenant, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].MatchedRuleId.Should().BeNull();
        results[0].NewCategoryId.Should().BeNull();
    }

    [Fact]
    public async Task Inactive_rules_must_be_filtered_by_repository()
    {
        // The engine consumes whatever ListActiveAsync returns. The
        // contract is that inactive (and soft-deleted) rules are
        // already excluded by the repository / global query filter.
        // This test asserts the *engine* respects that contract:
        // if an inactive rule somehow reaches the engine, it would
        // still match — so we instead verify the repo filtering is
        // the gate by passing an active rule that would match and an
        // inactive rule that would also match but isn't returned.
        var category = Guid.NewGuid();
        var activeRule = Rule("Active", "X", CategorizationMatchType.Contains, category, 0, active: true);
        var inactiveRule = Rule("Inactive", "X", CategorizationMatchType.Contains, Guid.NewGuid(), 0, active: false);

        // Repo's ListActiveAsync filters to active only.
        var repo = new StubRuleRepo(activeRule, inactiveRule);
        var engine = new CategorizationRuleEngine(repo);

        var results = await engine.ApplyAsync(
            new[] { new TransactionToCategorize(Guid.NewGuid(), "X") },
            Tenant, CancellationToken.None);

        results[0].NewCategoryId.Should().Be(category, "the inactive rule must be filtered upstream");
    }

    [Fact]
    public async Task Empty_description_returns_no_match()
    {
        var repo = new StubRuleRepo(
            Rule("R1", "anything", CategorizationMatchType.Contains, Guid.NewGuid(), 0));
        var engine = new CategorizationRuleEngine(repo);

        var results = await engine.ApplyAsync(
            new[]
            {
                new TransactionToCategorize(Guid.NewGuid(), ""),
                new TransactionToCategorize(Guid.NewGuid(), "   "),
            },
            Tenant, CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].MatchedRuleId.Should().BeNull();
        results[1].MatchedRuleId.Should().BeNull();
    }

    [Fact]
    public async Task Empty_rules_returns_no_match_for_every_input()
    {
        var repo = new StubRuleRepo();
        var engine = new CategorizationRuleEngine(repo);

        var results = await engine.ApplyAsync(
            new[]
            {
                new TransactionToCategorize(Guid.NewGuid(), "Continente"),
                new TransactionToCategorize(Guid.NewGuid(), "MEO"),
            },
            Tenant, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.MatchedRuleId == null && r.NewCategoryId == null);
    }

    private sealed class StubRuleRepo : ICategorizationRuleRepository
    {
        private readonly List<CategorizationRule> _rules;
        public StubRuleRepo(params CategorizationRule[] rules) => _rules = rules.ToList();

        public Task<IReadOnlyList<CategorizationRule>> ListActiveAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CategorizationRule>>(
                _rules.Where(r => r.IsActive && r.DeletedAt == null)
                      .OrderBy(r => r.Priority)
                      .ToList());

        public Task<IReadOnlyList<CategorizationRule>> ListAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CategorizationRule>>(_rules);

        public Task<CategorizationRule?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_rules.FirstOrDefault(r => r.Id == id));

        public Task AddAsync(CategorizationRule rule, CancellationToken ct)
        {
            _rules.Add(rule);
            return Task.CompletedTask;
        }

        public void Update(CategorizationRule rule) { /* tracked in-place */ }

        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);

        public Task<int> CountByPriorityAsync(int priority, Guid? excludeId, CancellationToken ct)
            => Task.FromResult(_rules.Count(r => r.Priority == priority && r.Id != excludeId));
    }
}
