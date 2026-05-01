using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Domain.CategorizationRules;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.CategorizationRules;

[NonTransactional]
public static class CategorizationRuleHandlers
{
    public static async Task<CategorizationRuleResponse> Handle(
        CreateCategorizationRuleCommand command,
        ICategorizationRuleRepository ruleRepo,
        ICategoryRepository categoryRepo,
        ITenantContext tenant,
        CancellationToken ct)
    {
        // Validate duplicate priority
        var existsSamePriority = await ruleRepo.CountByPriorityAsync(command.Priority, null, ct);
        if (existsSamePriority > 0)
            throw new CategorizationRuleDuplicatePriorityException(command.Priority);

        // Validate category exists
        var category = await categoryRepo.GetByIdAsync(command.CategoryId, ct);
        if (category is null)
            throw new InvalidOperationException("Categoria não encontrada.");

        var matchType = Enum.Parse<CategorizationMatchType>(command.MatchType);

        var rule = CategorizationRule.Create(
            command.Name,
            command.Pattern,
            matchType,
            command.CategoryId,
            command.Priority,
            tenant.TenantId);

        await ruleRepo.AddAsync(rule, ct);
        await ruleRepo.SaveChangesAsync(ct);
        return ToResponse(rule, category);
    }

    public static async Task<CategorizationRuleResponse?> Handle(
        UpdateCategorizationRuleCommand command,
        ICategorizationRuleRepository ruleRepo,
        ICategoryRepository categoryRepo,
        CancellationToken ct)
    {
        var rule = await ruleRepo.GetByIdAsync(command.Id, ct);
        if (rule is null) return null;

        // Validate duplicate priority (excluding self)
        var existsSamePriority = await ruleRepo.CountByPriorityAsync(command.Priority, command.Id, ct);
        if (existsSamePriority > 0)
            throw new CategorizationRuleDuplicatePriorityException(command.Priority);

        var category = await categoryRepo.GetByIdAsync(command.CategoryId, ct);
        if (category is null)
            throw new InvalidOperationException("Categoria não encontrada.");

        var matchType = Enum.Parse<CategorizationMatchType>(command.MatchType);

        rule.Update(
            command.Name,
            command.Pattern,
            matchType,
            command.CategoryId,
            command.Priority,
            command.IsActive);

        ruleRepo.Update(rule);
        await ruleRepo.SaveChangesAsync(ct);
        return ToResponse(rule, category);
    }

    public static async Task<bool> Handle(
        ArchiveCategorizationRuleCommand command,
        ICategorizationRuleRepository ruleRepo,
        CancellationToken ct)
    {
        var rule = await ruleRepo.GetByIdAsync(command.Id, ct);
        if (rule is null) return false;

        rule.Archive();
        ruleRepo.Update(rule);
        await ruleRepo.SaveChangesAsync(ct);
        return true;
    }

    public static async Task<CategorizationRuleResponse?> Handle(
        GetCategorizationRuleByIdQuery query,
        ICategorizationRuleRepository ruleRepo,
        ICategoryRepository categoryRepo,
        CancellationToken ct)
    {
        var rule = await ruleRepo.GetByIdAsync(query.Id, ct);
        if (rule is null) return null;

        var category = await categoryRepo.GetByIdAsync(rule.CategoryId, ct);
        return ToResponse(rule, category);
    }

    public static async Task<IReadOnlyList<CategorizationRuleResponse>> Handle(
        ListCategorizationRulesQuery query,
        ICategorizationRuleRepository ruleRepo,
        ICategoryRepository categoryRepo,
        CancellationToken ct)
    {
        var rules = await ruleRepo.ListAsync(ct);
        var categoryIds = rules.Select(r => r.CategoryId).Distinct().ToList();
        var categories = new Dictionary<Guid, Category>();
        foreach (var catId in categoryIds)
        {
            var cat = await categoryRepo.GetByIdAsync(catId, ct);
            if (cat is not null)
                categories[catId] = cat;
        }

        return rules
            .Select(r =>
            {
                categories.TryGetValue(r.CategoryId, out var cat);
                return ToResponse(r, cat);
            })
            .ToList();
    }

    public static async Task<bool> Handle(
        ReorderCategorizationRulesCommand command,
        ICategorizationRuleRepository ruleRepo,
        CancellationToken ct)
    {
        for (int i = 0; i < command.RuleIds.Count; i++)
        {
            var rule = await ruleRepo.GetByIdAsync(command.RuleIds[i], ct);
            if (rule is not null)
            {
                rule.SetPriority(i + 1);
                ruleRepo.Update(rule);
            }
        }
        await ruleRepo.SaveChangesAsync(ct);
        return true;
    }

    public static async Task<ReapplyCategorizationRulesResponse> Handle(
        ReapplyCategorizationRulesCommand command,
        ITransactionRepository txRepo,
        ICategorizationRuleEngine engine,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var filter = new TransactionFilter(
            command.From,
            command.To,
            command.CategoryId is not null ? new[] { command.CategoryId.Value } : null,
            null,
            null,
            10000,
            null);

        var page = await txRepo.ListAsync(filter, ct);
        var allTransactions = page.Items;

        if (command.OnlyUncategorized)
        {
            allTransactions = allTransactions.Where(t => t.CategorizationRuleId is null).ToList();
        }

        var toProcess = allTransactions
            .Where(t => !string.IsNullOrWhiteSpace(t.Description))
            .Select(t => new TransactionToCategorize(t.Id, t.Description!))
            .ToList();

        var results = await engine.ApplyAsync(toProcess, tenant.TenantId, ct);

        var categorizedCount = 0;
        foreach (var result in results)
        {
            if (result.NewCategoryId is null) continue;

            var tx = allTransactions.FirstOrDefault(t => t.Id == result.TransactionId);
            if (tx is null) continue;

            if (tx.CategoryId == result.NewCategoryId.Value) continue;

            tx.SetCategory(result.NewCategoryId.Value);
            if (result.MatchedRuleId is not null)
                tx.MarkCategorizedByRule(result.MatchedRuleId.Value);

            txRepo.Update(tx);
            categorizedCount++;
        }

        await txRepo.SaveChangesAsync(ct);

        return new ReapplyCategorizationRulesResponse(
            toProcess.Count,
            categorizedCount,
            toProcess.Count - categorizedCount);
    }

    private static CategorizationRuleResponse ToResponse(CategorizationRule rule, Category? category)
        => new(
            rule.Id,
            rule.Name,
            rule.Pattern,
            rule.MatchType.ToString(),
            rule.CategoryId,
            category?.Name ?? "—",
            category?.IconName ?? "pi-question",
            category?.ColorHex ?? "#64748B",
            rule.Priority,
            rule.IsActive,
            rule.CreatedAt,
            rule.UpdatedAt);
}
