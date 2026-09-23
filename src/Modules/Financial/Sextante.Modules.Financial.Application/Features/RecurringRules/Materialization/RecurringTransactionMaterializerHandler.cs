using Sextante.Modules.Financial.Application.Common;
using Sextante.Modules.Financial.Application.ExchangeRates;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.RecurringRules;
using Sextante.Modules.Financial.Domain.Transactions;
using Sextante.Modules.Financial.PublicApi.Events;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;
using Microsoft.Extensions.Logging;

namespace Sextante.Modules.Financial.Application.Features.RecurringRules.Materialization;

public sealed class RecurringTransactionMaterializerHandler
{
    private readonly IRecurringRuleRepository _ruleRepo;
    private readonly ITransactionRepository _txRepo;
    private readonly ICategoryRepository _categoryRepo;
    private readonly IExchangeRateService _exchangeRateService;
    private readonly ITenantCurrencyResolver _currencyResolver;
    private readonly IIntegrationEventPublisher _events;
    private readonly ILogger<RecurringTransactionMaterializerHandler> _logger;

    public RecurringTransactionMaterializerHandler(
        IRecurringRuleRepository ruleRepo,
        ITransactionRepository txRepo,
        ICategoryRepository categoryRepo,
        IExchangeRateService exchangeRateService,
        ITenantCurrencyResolver currencyResolver,
        IIntegrationEventPublisher events,
        ILogger<RecurringTransactionMaterializerHandler> logger)
    {
        _ruleRepo = ruleRepo;
        _txRepo = txRepo;
        _categoryRepo = categoryRepo;
        _exchangeRateService = exchangeRateService;
        _currencyResolver = currencyResolver;
        _events = events;
        _logger = logger;
    }

    public async Task<RecurringMaterializerResult> ExecuteAsync(
        RecurringMaterializerPayload payload,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var runDate = payload.RunDate;

        var rules = await _ruleRepo.GetActiveRulesDueAsync(runDate, ct);

        if (rules.Count == 0)
        {
            return new RecurringMaterializerResult(0, 0, 0, 0, 0);
        }

        var primaryCurrency = await _currencyResolver.GetPrimaryCurrencyAsync(ct);

        var materialized = 0;
        var skippedDuplicate = 0;
        var skippedNoRate = 0;
        var completed = 0;
        var publishQueue = new List<TransactionCreatedIntegrationEvent>();

        foreach (var rule in rules)
        {
            while (rule.NextOccurrence is not null && rule.NextOccurrence.Value <= runDate)
            {
                var occurrenceDate = rule.NextOccurrence.Value;

                if (await _ruleRepo.HasMaterializedAsync(rule.Id, occurrenceDate, ct))
                {
                    _logger.LogDebug(
                        "Recurring rule {RuleId} occurrence {Date} já materializada — skip",
                        rule.Id, occurrenceDate);
                    skippedDuplicate++;
                }
                else
                {
                    try
                    {
                        // Resolver exchange rate
                        ExchangeRateSnapshot? snapshot = null;
                        if (!string.Equals(rule.Amount.Currency, primaryCurrency, StringComparison.Ordinal))
                        {
                            try
                            {
                                snapshot = await _exchangeRateService.ResolveAsync(
                                    rule.Amount.Currency,
                                    primaryCurrency,
                                    occurrenceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                                    ct);
                            }
                            catch (ExchangeRateUnavailableException)
                            {
                                _logger.LogWarning(
                                    "Sem taxa de câmbio para {Currency} → {Primary} em {Date}. " +
                                    "Regra {RuleId} '{Description}' — ocorrência {OccurrenceDate} skip.",
                                    rule.Amount.Currency, primaryCurrency, occurrenceDate,
                                    rule.Id, rule.Description, occurrenceDate);
                                skippedNoRate++;
                                break;
                            }
                        }

                        // A Transaction armazena sempre Amount > 0; o sentido
                        // (entrada/saída) é a Direction (Phase 6.5, ADR-014).
                        var signedAmount = DetermineSignedAmount(rule);

                        var occurredAt = new DateTimeOffset(
                            occurrenceDate.Year, occurrenceDate.Month, occurrenceDate.Day,
                            0, 0, 0, TimeSpan.Zero);

                        var category = rule.CategoryId is { } categoryId
                            ? await _categoryRepo.GetByIdAsync(categoryId, ct)
                            : null;

                        Transaction transaction;
                        if (category is not null)
                        {
                            transaction = Transaction.CreateRegular(
                                rule.AccountId,
                                category.Id,
                                category.Kind,
                                occurredAt,
                                new Money(signedAmount, rule.Amount.Currency),
                                rule.Description,
                                rule.Tags.Count > 0 ? rule.Tags : null,
                                tenant.TenantId,
                                snapshot,
                                recurringRuleId: rule.Id);
                        }
                        else
                        {
                            // Regra sem categoria (ou categoria arquivada): a
                            // transação fica sem categoria e, como a regra não
                            // tem sentido próprio, conta como saída. Regras de
                            // categorização podem classificá-la depois.
                            _logger.LogDebug(
                                "Regra {RuleId} sem categoria ativa — transação sem categoria, saída",
                                rule.Id);
                            transaction = Transaction.CreateUncategorized(
                                rule.AccountId,
                                TransactionDirection.Outflow,
                                occurredAt,
                                new Money(signedAmount, rule.Amount.Currency),
                                rule.Description,
                                rule.Tags.Count > 0 ? rule.Tags : null,
                                tenant.TenantId,
                                snapshot,
                                recurringRuleId: rule.Id);
                        }

                        await _txRepo.AddAsync(transaction, ct);
                        publishQueue.Add(new TransactionCreatedIntegrationEvent(
                            transaction.Id,
                            tenant.TenantId.Value,
                            transaction.AccountId,
                            transaction.CategoryId,
                            transaction.Amount.Amount,
                            transaction.Amount.Currency,
                            transaction.OccurredAt,
                            DateTimeOffset.UtcNow));
                        materialized++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Erro ao materializar regra {RuleId} ocorrência {Date}",
                            rule.Id, occurrenceDate);
                        throw;
                    }
                }

                var isCompleted = rule.AdvanceNextOccurrence();
                _ruleRepo.Update(rule);

                if (isCompleted)
                {
                    completed++;
                    break;
                }
            }
        }

        await _txRepo.SaveChangesAsync(ct);

        // Publica eventos pós-commit (mesmo padrão SignupEndpoint).
        // Phase 5b: BudgetAlertDispatchHandler subscreve.
        foreach (var @event in publishQueue)
        {
            await _events.PublishAsync(@event, ct);
        }

        _logger.LogInformation(
            "Materializer tenant={TenantId}: {Total} regras, {Materialized} materializadas, " +
            "{SkippedDuplicate} duplicados, {SkippedNoRate} sem rate, {Completed} completas",
            tenant.TenantId.Value, rules.Count, materialized, skippedDuplicate, skippedNoRate, completed);

        return new RecurringMaterializerResult(
            rules.Count, materialized, skippedDuplicate, skippedNoRate, completed);
    }

    /// <summary>
    /// Devolve o valor absoluto do Amount da regra.
    /// A Transaction armazena sempre valores positivos; o sinal
    /// (negativo para Expense, positivo para Income) é derivado
    /// do Category.Kind no read-side (dashboard, summaries).
    /// </summary>
    private static decimal DetermineSignedAmount(RecurringRule rule)
    {
        return Math.Abs(rule.Amount.Amount);
    }
}
