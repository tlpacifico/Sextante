using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.RecurringRules;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.RecurringRules;

[NonTransactional]
public static class RecurringRuleHandlers
{
    public static async Task<RecurringRuleResponse> Handle(
        CreateRecurringRuleCommand command,
        IRecurringRuleRepository repository,
        IAccountRepository accountRepository,
        ITenantContext tenant,
        ICurrencyDirectory currencyDirectory,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Frequency>(command.Frequency, ignoreCase: true, out var frequency))
        {
            throw new RecurringRuleInvalidFrequencyException(command.Frequency);
        }

        if (!await currencyDirectory.IsActiveAsync(command.Currency, cancellationToken))
        {
            throw new CurrencyNotActiveException(command.Currency);
        }

        // Valida que a conta existe e pertence ao tenant.
        var account = await accountRepository.GetByIdAsync(command.AccountId, cancellationToken)
            ?? throw new ArgumentException("Conta não encontrada.");

        var rule = RecurringRule.Create(
            command.Description,
            new Money(command.Amount, command.Currency),
            command.AccountId,
            command.CategoryId,
            frequency,
            command.Interval,
            command.StartDate,
            command.EndDate,
            command.Tags,
            tenant.TenantId);

        await repository.AddAsync(rule, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return ToResponse(rule);
    }

    public static async Task<RecurringRuleResponse?> Handle(
        UpdateRecurringRuleCommand command,
        IRecurringRuleRepository repository,
        IAccountRepository accountRepository,
        ICurrencyDirectory currencyDirectory,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        if (!Enum.TryParse<Frequency>(command.Frequency, ignoreCase: true, out var frequency))
        {
            throw new RecurringRuleInvalidFrequencyException(command.Frequency);
        }

        if (!await currencyDirectory.IsActiveAsync(command.Currency, cancellationToken))
        {
            throw new CurrencyNotActiveException(command.Currency);
        }

        // Valida que a conta existe e pertence ao tenant.
        var account = await accountRepository.GetByIdAsync(command.AccountId, cancellationToken)
            ?? throw new ArgumentException("Conta não encontrada.");

        rule.Update(
            command.Description,
            new Money(command.Amount, command.Currency),
            command.AccountId,
            command.CategoryId,
            frequency,
            command.Interval,
            command.StartDate,
            command.EndDate,
            command.IsActive,
            command.Tags);

        repository.Update(rule);
        await repository.SaveChangesAsync(cancellationToken);
        return ToResponse(rule);
    }

    public static async Task<bool> Handle(
        ArchiveRecurringRuleCommand command,
        IRecurringRuleRepository repository,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (rule is null)
        {
            return false;
        }

        rule.Archive();
        repository.Update(rule);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static async Task<RecurringRuleResponse?> Handle(
        GetRecurringRuleByIdQuery query,
        IRecurringRuleRepository repository,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(query.Id, cancellationToken);
        return rule is null ? null : ToResponse(rule);
    }

    public static async Task<IReadOnlyList<RecurringRuleResponse>> Handle(
        ListRecurringRulesQuery _,
        IRecurringRuleRepository repository,
        CancellationToken cancellationToken)
    {
        var rules = await repository.ListAsync(cancellationToken);
        return rules.Select(ToResponse).ToList();
    }

    public static async Task<IReadOnlyList<DateOnly>> Handle(
        GetUpcomingOccurrencesQuery query,
        IRecurringRuleRepository repository,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(query.RuleId, cancellationToken);
        if (rule is null)
        {
            return Array.Empty<DateOnly>();
        }

        var count = Math.Clamp(query.Count, 1, 50);
        return rule.GetUpcomingOccurrences(count);
    }

    private static RecurringRuleResponse ToResponse(RecurringRule rule)
        => new(
            rule.Id,
            rule.Description,
            rule.Amount,
            rule.AccountId,
            rule.CategoryId,
            rule.Frequency.ToString(),
            rule.Interval,
            rule.StartDate,
            rule.EndDate,
            rule.NextOccurrence,
            rule.IsActive,
            rule.Tags.ToList(),
            rule.CreatedAt,
            rule.UpdatedAt);
}
