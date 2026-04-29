using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Transactions;

public sealed class Transaction : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int DescriptionMaxLength = 500;
    public const int MaxTags = 10;
    public const int TagMaxLength = 50;

    private readonly List<string> _tags = new();

    private Transaction()
    {
        Description = null;
        Amount = null!;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid CategoryId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Money Amount { get; private set; }
    public string? Description { get; private set; }
    public IReadOnlyList<string> Tags => _tags;

    /// <summary>
    /// Câmbio gravado no momento da criação. Phase 3+: <c>null</c>
    /// quando <c>Amount.Currency == tenant primary</c> (read-side
    /// interpreta como <c>1.0 implied</c>) ou em rows pré-Phase-3
    /// (sem backfill).
    /// </summary>
    public decimal? ExchangeRateToPrimary { get; private set; }

    public DateTimeOffset? ExchangeRateAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static Transaction Create(
        Guid accountId,
        Guid categoryId,
        DateTimeOffset occurredAt,
        Money amount,
        string? description,
        IEnumerable<string>? tags,
        TenantId tenantId,
        ExchangeRateSnapshot? exchangeRate = null,
        DateTimeOffset? now = null)
    {
        if (amount.Amount <= 0m)
        {
            throw new TransactionAmountMustBePositiveException();
        }

        var reference = now ?? DateTimeOffset.UtcNow;
        if (occurredAt > reference.AddMinutes(1))
        {
            throw new TransactionInFutureException();
        }

        var normalized = NormalizeDescription(description);
        var normalizedTags = NormalizeTags(tags);

        var transaction = new Transaction
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            AccountId = accountId,
            CategoryId = categoryId,
            OccurredAt = occurredAt,
            Amount = amount,
            Description = normalized,
            ExchangeRateToPrimary = exchangeRate?.Rate,
            ExchangeRateAt = exchangeRate?.At,
        };
        transaction._tags.AddRange(normalizedTags);
        return transaction;
    }

    public void Update(
        Guid accountId,
        Guid categoryId,
        DateTimeOffset occurredAt,
        Money amount,
        string? description,
        IEnumerable<string>? tags,
        DateTimeOffset? now = null)
    {
        if (amount.Amount <= 0m)
        {
            throw new TransactionAmountMustBePositiveException();
        }

        var reference = now ?? DateTimeOffset.UtcNow;
        if (occurredAt > reference.AddMinutes(1))
        {
            throw new TransactionInFutureException();
        }

        AccountId = accountId;
        CategoryId = categoryId;
        OccurredAt = occurredAt;
        Amount = amount;
        Description = NormalizeDescription(description);
        // ExchangeRateToPrimary / ExchangeRateAt são frozen — não
        // recomputados em Update (decisão registada em
        // requirements.md "Decisions").

        _tags.Clear();
        _tags.AddRange(NormalizeTags(tags));
    }

    public void Archive()
    {
        DeletedAt = DateTimeOffset.UtcNow;
    }

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        if (trimmed.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Descrição tem no máximo {DescriptionMaxLength} caracteres.",
                nameof(description));
        }

        return trimmed;
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return new List<string>();
        }

        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length > TagMaxLength)
            {
                throw new ArgumentException(
                    $"Tag tem no máximo {TagMaxLength} caracteres.",
                    nameof(tags));
            }

            if (!seen.Add(trimmed))
            {
                throw new ArgumentException("Tags duplicadas não são permitidas.", nameof(tags));
            }

            list.Add(trimmed);

            if (list.Count > MaxTags)
            {
                throw new ArgumentException(
                    $"No máximo {MaxTags} tags por transação.",
                    nameof(tags));
            }
        }

        return list;
    }
}
