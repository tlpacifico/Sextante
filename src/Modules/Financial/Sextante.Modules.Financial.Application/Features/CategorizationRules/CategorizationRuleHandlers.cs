using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Domain.Accounts;
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
        IAccountRepository accountRepo,
        ITenantContext tenant,
        CancellationToken ct)
    {
        // Validate duplicate priority
        var existsSamePriority = await ruleRepo.CountByPriorityAsync(command.Priority, null, ct);
        if (existsSamePriority > 0)
            throw new CategorizationRuleDuplicatePriorityException(command.Priority);

        var matchType = Enum.Parse<CategorizationMatchType>(command.MatchType);
        var action = Enum.Parse<RuleAction>(command.Action);

        if (action == RuleAction.MarkAsTransfer)
        {
            var target = await GetTargetAccountAsync(command.TargetAccountId, accountRepo, ct);
            var transferRule = CategorizationRule.CreateTransfer(
                command.Name,
                command.Pattern,
                matchType,
                target.Id,
                command.Priority,
                tenant.TenantId);

            await ruleRepo.AddAsync(transferRule, ct);
            await ruleRepo.SaveChangesAsync(ct);
            return ToResponse(transferRule, null, target);
        }

        var category = await GetCategoryAsync(command.CategoryId, categoryRepo, ct);

        var rule = CategorizationRule.Create(
            command.Name,
            command.Pattern,
            matchType,
            category.Id,
            command.Priority,
            tenant.TenantId);

        await ruleRepo.AddAsync(rule, ct);
        await ruleRepo.SaveChangesAsync(ct);
        return ToResponse(rule, category, null);
    }

    public static async Task<CategorizationRuleResponse?> Handle(
        UpdateCategorizationRuleCommand command,
        ICategorizationRuleRepository ruleRepo,
        ICategoryRepository categoryRepo,
        IAccountRepository accountRepo,
        CancellationToken ct)
    {
        var rule = await ruleRepo.GetByIdAsync(command.Id, ct);
        if (rule is null) return null;

        // Validate duplicate priority (excluding self)
        var existsSamePriority = await ruleRepo.CountByPriorityAsync(command.Priority, command.Id, ct);
        if (existsSamePriority > 0)
            throw new CategorizationRuleDuplicatePriorityException(command.Priority);

        var matchType = Enum.Parse<CategorizationMatchType>(command.MatchType);
        var action = Enum.Parse<RuleAction>(command.Action);

        Category? category = null;
        Account? target = null;
        if (action == RuleAction.MarkAsTransfer)
        {
            target = await GetTargetAccountAsync(command.TargetAccountId, accountRepo, ct);
            rule.UpdateAsTransfer(
                command.Name,
                command.Pattern,
                matchType,
                target.Id,
                command.Priority,
                command.IsActive);
        }
        else
        {
            category = await GetCategoryAsync(command.CategoryId, categoryRepo, ct);
            rule.Update(
                command.Name,
                command.Pattern,
                matchType,
                category.Id,
                command.Priority,
                command.IsActive);
        }

        ruleRepo.Update(rule);
        await ruleRepo.SaveChangesAsync(ct);
        return ToResponse(rule, category, target);
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
        IAccountRepository accountRepo,
        CancellationToken ct)
    {
        var rule = await ruleRepo.GetByIdAsync(query.Id, ct);
        if (rule is null) return null;

        var category = rule.CategoryId is { } categoryId
            ? await categoryRepo.GetByIdAsync(categoryId, ct)
            : null;
        var target = rule.TargetAccountId is { } targetId
            ? await accountRepo.GetByIdAsync(targetId, ct)
            : null;
        return ToResponse(rule, category, target);
    }

    public static async Task<IReadOnlyList<CategorizationRuleResponse>> Handle(
        ListCategorizationRulesQuery query,
        ICategorizationRuleRepository ruleRepo,
        ICategoryRepository categoryRepo,
        IAccountRepository accountRepo,
        CancellationToken ct)
    {
        var rules = await ruleRepo.ListAsync(ct);
        var categoryIds = rules
            .Where(r => r.CategoryId is not null)
            .Select(r => r.CategoryId!.Value)
            .Distinct()
            .ToList();
        var categories = new Dictionary<Guid, Category>();
        foreach (var catId in categoryIds)
        {
            var cat = await categoryRepo.GetByIdAsync(catId, ct);
            if (cat is not null)
                categories[catId] = cat;
        }

        var accounts = rules.Any(r => r.TargetAccountId is not null)
            ? (await accountRepo.ListAsync(ct)).ToDictionary(a => a.Id)
            : new Dictionary<Guid, Account>();

        return rules
            .Select(r =>
            {
                Category? cat = null;
                if (r.CategoryId is { } categoryId)
                    categories.TryGetValue(categoryId, out cat);
                Account? target = null;
                if (r.TargetAccountId is { } targetId)
                    accounts.TryGetValue(targetId, out target);
                return ToResponse(r, cat, target);
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
        IAccountRepository accountRepo,
        ITransferCounterpartQuery transferQuery,
        ITenantCurrencyResolver currency,
        IExchangeRateService exchangeRates,
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
        var claimed = new HashSet<Guid>();
        var transfers = 0;
        foreach (var result in results)
        {
            if (!byId.TryGetValue(result.TransactionId, out var tx)) continue;

            if (result.TargetAccountId is { } targetAccountId)
            {
                if (await TryConvertToTransferAsync(
                        tx, targetAccountId, claimed, txRepo, accountRepo, transferQuery,
                        tenant, currency, exchangeRates, events, ct))
                {
                    transfers++;
                }

                continue;
            }

            if (result.NewCategoryId is null) continue;

            // Pode ter passado a perna de uma transferência convertida acima.
            if (tx.Kind != TransactionKind.Regular) continue;

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
            toProcess.Count - changed.Count - transfers,
            transfers);
    }

    private const int ReapplyPageSize = 100;

    /// <summary>
    /// Phase 6.5 grupo 7 (R8) — regra MarkAsTransfer sobre uma transação
    /// existente: liga à contraperna na conta alvo (D9) ou cria-a, reusando o
    /// ConvertToTransferCommand do grupo 3 (validações, eventos, câmbio).
    /// </summary>
    private static async Task<bool> TryConvertToTransferAsync(
        Transaction tx,
        Guid targetAccountId,
        HashSet<Guid> claimed,
        ITransactionRepository txRepo,
        IAccountRepository accountRepo,
        ITransferCounterpartQuery transferQuery,
        ITenantContext tenant,
        ITenantCurrencyResolver currency,
        IExchangeRateService exchangeRates,
        IIntegrationEventPublisher events,
        CancellationToken ct)
    {
        // Pode já ter sido a contraperna de outra transação desta passagem.
        if (tx.Kind != TransactionKind.Regular || tx.AccountId == targetAccountId) return false;

        var target = await accountRepo.GetByIdAsync(targetAccountId, ct);
        if (target is null) return false;

        var date = DateOnly.FromDateTime(tx.OccurredAt.UtcDateTime);
        var opposite = tx.Direction == TransactionDirection.Outflow
            ? TransactionDirection.Inflow
            : TransactionDirection.Outflow;
        var candidates = await transferQuery.FindAsync(
            target.Id,
            TransactionKind.Regular,
            opposite,
            tx.Amount.Amount,
            tx.Amount.Currency,
            date.AddDays(-TransferCounterpartMatcher.WindowDays),
            date.AddDays(TransferCounterpartMatcher.WindowDays),
            ct);
        var pick = TransferCounterpartMatcher.Pick(candidates, date, claimed);

        try
        {
            await TransferHandlers.Handle(
                new ConvertToTransferCommand(tx.Id, target.Id, pick?.TransactionId),
                txRepo, accountRepo, tenant, currency, exchangeRates, events, ct);
        }
        catch (FinancialDomainException)
        {
            // Ex.: moedas diferentes sem contraperna — fica regular.
            return false;
        }

        claimed.Add(tx.Id);
        if (pick is not null)
            claimed.Add(pick.TransactionId);
        return true;
    }

    private static async Task<Category> GetCategoryAsync(
        Guid? categoryId, ICategoryRepository categoryRepo, CancellationToken ct)
    {
        if (categoryId is null || categoryId == Guid.Empty)
            throw new CategorizationRuleCategoryRequiredException();

        return await categoryRepo.GetByIdAsync(categoryId.Value, ct)
            ?? throw new InvalidOperationException("Categoria não encontrada.");
    }

    private static async Task<Account> GetTargetAccountAsync(
        Guid? targetAccountId, IAccountRepository accountRepo, CancellationToken ct)
    {
        if (targetAccountId is null || targetAccountId == Guid.Empty)
            throw new CategorizationRuleTargetAccountRequiredException();

        return await accountRepo.GetByIdAsync(targetAccountId.Value, ct)
            ?? throw new KeyNotFoundException("Conta de destino não encontrada.");
    }

    private static CategorizationRuleResponse ToResponse(CategorizationRule rule, Category? category, Account? target)
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
            rule.UpdatedAt,
            rule.Action.ToString(),
            rule.TargetAccountId,
            target?.Name);
}
