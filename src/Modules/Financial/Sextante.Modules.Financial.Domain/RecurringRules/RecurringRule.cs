using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.RecurringRules;

public sealed class RecurringRule : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int DescriptionMaxLength = 256;

    private readonly List<string> _tags = new();

    private RecurringRule()
    {
        Description = string.Empty;
        Amount = null!;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Description { get; private set; }
    public Money Amount { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Frequency Frequency { get; private set; }
    public int Interval { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public DateOnly? NextOccurrence { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyList<string> Tags => _tags;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static RecurringRule Create(
        string description,
        Money amount,
        Guid accountId,
        Guid? categoryId,
        Frequency frequency,
        int interval,
        DateOnly startDate,
        DateOnly? endDate,
        IEnumerable<string>? tags,
        TenantId tenantId,
        DateOnly? today = null)
    {
        Validate(description, amount, interval, startDate, endDate, frequency);

        var rule = new RecurringRule
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            Description = description.Trim(),
            Amount = amount,
            AccountId = accountId,
            CategoryId = categoryId,
            Frequency = frequency,
            Interval = interval,
            StartDate = startDate,
            EndDate = endDate,
            IsActive = true,
        };

        if (tags is not null)
        {
            rule._tags.AddRange(NormalizeTags(tags));
        }

        rule.NextOccurrence = CalculateInitialNextOccurrence(rule, today ?? DateOnly.FromDateTime(DateTime.UtcNow));
        return rule;
    }

    public void Update(
        string description,
        Money amount,
        Guid accountId,
        Guid? categoryId,
        Frequency frequency,
        int interval,
        DateOnly startDate,
        DateOnly? endDate,
        bool isActive,
        IEnumerable<string>? tags,
        DateOnly? today = null)
    {
        Validate(description, amount, interval, startDate, endDate, frequency);

        var changedSchedule =
            StartDate != startDate
            || Frequency != frequency
            || Interval != interval
            || EndDate != endDate;

        Description = description.Trim();
        Amount = amount;
        AccountId = accountId;
        CategoryId = categoryId;
        Frequency = frequency;
        Interval = interval;
        StartDate = startDate;
        EndDate = endDate;
        IsActive = isActive;

        _tags.Clear();
        if (tags is not null)
        {
            _tags.AddRange(NormalizeTags(tags));
        }

        if (changedSchedule)
        {
            NextOccurrence = CalculateInitialNextOccurrence(this, today ?? DateOnly.FromDateTime(DateTime.UtcNow));
        }
    }

    public void Archive()
    {
        DeletedAt = DateTimeOffset.UtcNow;
    }

    public bool AdvanceNextOccurrence()
    {
        if (NextOccurrence is null)
        {
            return true;
        }

        var current = NextOccurrence.Value;

        var next = Frequency switch
        {
            Frequency.Daily => current.AddDays(Interval),
            Frequency.Weekly => current.AddDays(Interval * 7),
            Frequency.Monthly => ClampDayOfMonth(current.AddMonths(Interval)),
            Frequency.Yearly => ClampDayOfMonth(current.AddYears(Interval)),
            _ => current,
        };

        if (EndDate is not null && next > EndDate.Value)
        {
            NextOccurrence = null;
            return true;
        }

        NextOccurrence = next;
        return false;
    }

    public IReadOnlyList<DateOnly> GetUpcomingOccurrences(int count)
    {
        var results = new List<DateOnly>(count);

        if (NextOccurrence is null)
        {
            return results;
        }

        // Clone state in-memory via copy constructor e chama
        // AdvanceNextOccurrence em loop (puro — não persiste).
        var clone = new RecurringRule
        {
            Id = Id,
            TenantId = TenantId,
            Description = Description,
            Amount = Amount,
            AccountId = AccountId,
            CategoryId = CategoryId,
            Frequency = Frequency,
            Interval = Interval,
            StartDate = StartDate,
            EndDate = EndDate,
            NextOccurrence = NextOccurrence,
            IsActive = IsActive,
        };

        for (var i = 0; i < count; i++)
        {
            if (clone.NextOccurrence is null)
            {
                break;
            }

            results.Add(clone.NextOccurrence.Value);
            clone.AdvanceNextOccurrence();
        }

        return results;
    }

    private static void Validate(
        string description,
        Money amount,
        int interval,
        DateOnly startDate,
        DateOnly? endDate,
        Frequency frequency)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new RecurringRuleDescriptionRequiredException();
        }

        if (description.Trim().Length > DescriptionMaxLength)
        {
            throw new RecurringRuleDescriptionTooLongException(DescriptionMaxLength);
        }

        if (amount.Amount <= 0m)
        {
            throw new RecurringRuleAmountMustBePositiveException();
        }

        if (interval < 1)
        {
            throw new RecurringRuleIntervalMustBePositiveException();
        }

        if (endDate is not null && startDate > endDate.Value)
        {
            throw new RecurringRuleStartDateAfterEndDateException();
        }

        if (frequency is not (Frequency.Daily or Frequency.Weekly or Frequency.Monthly or Frequency.Yearly))
        {
            throw new RecurringRuleInvalidFrequencyException(frequency.ToString());
        }
    }

    private static DateOnly? CalculateInitialNextOccurrence(RecurringRule rule, DateOnly today)
    {
        var cursor = rule.StartDate;

        if (cursor >= today)
        {
            if (rule.EndDate is not null && cursor > rule.EndDate.Value)
            {
                return null;
            }

            return cursor;
        }

        while (cursor <= today)
        {
            cursor = rule.Frequency switch
            {
                Frequency.Daily => cursor.AddDays(rule.Interval),
                Frequency.Weekly => cursor.AddDays(rule.Interval * 7),
                Frequency.Monthly => ClampDayOfMonth(cursor.AddMonths(rule.Interval)),
                Frequency.Yearly => ClampDayOfMonth(cursor.AddYears(rule.Interval)),
                _ => cursor,
            };

            if (rule.EndDate is not null && cursor > rule.EndDate.Value)
            {
                return null;
            }
        }

        return cursor;
    }

    /// <summary>
    /// Clamp o dia ao último dia do mês se o dia original é
    /// maior que o número de dias do novo mês (ex.: 31-Jan
    /// + 1 month → 28-Fev em ano não-bissexto).
    /// </summary>
    private static DateOnly ClampDayOfMonth(DateOnly target)
    {
        var daysInMonth = DateTime.DaysInMonth(target.Year, target.Month);
        if (target.Day > daysInMonth)
        {
            return new DateOnly(target.Year, target.Month, daysInMonth);
        }

        return target;
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length > 50)
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                list.Add(trimmed);

                if (list.Count >= 10)
                {
                    break;
                }
            }
        }

        return list;
    }
}

public sealed class RecurringRuleDescriptionRequiredException : FinancialDomainException
{
    public RecurringRuleDescriptionRequiredException()
        : base("A descrição da regra é obrigatória.") { }
}

public sealed class RecurringRuleDescriptionTooLongException : FinancialDomainException
{
    public RecurringRuleDescriptionTooLongException(int max)
        : base($"A descrição da regra tem no máximo {max} caracteres.") { }
}

public sealed class RecurringRuleAmountMustBePositiveException : FinancialDomainException
{
    public RecurringRuleAmountMustBePositiveException()
        : base("O valor da regra tem de ser maior que zero.") { }
}

public sealed class RecurringRuleIntervalMustBePositiveException : FinancialDomainException
{
    public RecurringRuleIntervalMustBePositiveException()
        : base("O intervalo tem de ser maior ou igual a 1.") { }
}

public sealed class RecurringRuleStartDateAfterEndDateException : FinancialDomainException
{
    public RecurringRuleStartDateAfterEndDateException()
        : base("A data de início não pode ser superior à data de fim.") { }
}

public sealed class RecurringRuleInvalidFrequencyException : FinancialDomainException
{
    public RecurringRuleInvalidFrequencyException(string value)
        : base($"Frequência '{value}' inválida. Use Daily, Weekly, Monthly ou Yearly.") { }
}
