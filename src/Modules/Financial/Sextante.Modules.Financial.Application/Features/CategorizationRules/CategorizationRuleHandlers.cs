using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Domain.CategorizationRules;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.PublicApi.Events;
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
        ICategoryRepository categoryRepo,
        ICategorizationRuleEngine engine,
        ITenantContext tenant,
        IIntegrationEventPublisher events,
        CancellationToken ct)
    {
        // Phase 6.5 §0.3 — o repositório limita cada página a 100 linhas;
        // percorrer por cursor até ao fim em vez de pedir PageSize=10000.
        var categoryIds = command.CategoryId is not null ? new[] { command.CategoryId.Value } : null;
        var allTransactions = new List<Transaction>();
        TransactionCursor? cursor = null;
        do
        {
            var page = await txRepo.ListAsync(
                new TransactionFilter(command.From, command.To, categoryIds, null, null, ReapplyPageSize, cursor),
                ct);
            allTransactions.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        if (command.OnlyUncategorized)
        {
            allTransactions = allTransactions.Where(t => t.CategorizationRuleId is null).ToList();
        }

        // Transferências e acertos não têm categoria e não podem ser
        // recategorizados (Transaction.SetCategory lança
        // TransactionNotRegularException) — nunca entram no motor de regras.
        var toProcess = allTransactions
            .Where(t => t.Kind == TransactionKind.Regular && !string.IsNullOrWhiteSpace(t.Description))
            .Select(t => new TransactionToCategorize(t.Id, t.Description!))
            .ToList();

        var results = await engine.ApplyAsync(toProcess, tenant.TenantId, ct);

        // A direção segue o tipo da categoria; regras que apontam para uma
        // categoria arquivada não recategorizam (ficam como inalteradas).
        var kinds = (await categoryRepo.ListAsync(ct)).ToDictionary(c => c.Id, c => c.Kind);
        var byId = allTransactions.ToDictionary(t => t.Id);
        var changed = new List<Transaction>();
        foreach (var result in results)
        {
            if (result.NewCategoryId is null) continue;

            if (!byId.TryGetValue(result.TransactionId, out var tx)) continue;

            if (tx.CategoryId == result.NewCategoryId.Value) continue;

            if (!kinds.TryGetValue(result.NewCategoryId.Value, out var kind)) continue;

            tx.SetCategory(result.NewCategoryId.Value, kind);
            if (result.MatchedRuleId is not null)
                tx.MarkCategorizedByRule(result.MatchedRuleId.Value);

            txRepo.Update(tx);
            changed.Add(tx);
        }

        await txRepo.SaveChangesAsync(ct);

        // Orçamentos recalculam a partir destes eventos (Phase 5b).
        foreach (var tx in changed)
        {
            await events.PublishAsync(
                new TransactionUpdatedIntegrationEvent(
                    tx.Id,
                    tenant.TenantId.Value,
                    tx.AccountId,
                    tx.CategoryId,
                    tx.Amount.Amount,
                    tx.Amount.Currency,
                    tx.OccurredAt,
                    DateTimeOffset.UtcNow),
                ct);
        }

        return new ReapplyCategorizationRulesResponse(
            toProcess.Count,
            changed.Count,
            toProcess.Count - changed.Count);
    }

    private const int ReapplyPageSize = 100;

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
