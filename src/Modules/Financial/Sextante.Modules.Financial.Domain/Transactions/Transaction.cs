using Sextante.Modules.Financial.Domain.Categories;
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
    /// <summary>
    /// <c>null</c> em transferências, acertos e transações regulares ainda
    /// sem categoria.
    /// </summary>
    public Guid? CategoryId { get; private set; }
    public TransactionDirection Direction { get; private set; }
    public TransactionKind Kind { get; private set; }

    /// <summary>
    /// Liga as duas pernas de uma transferência (Phase 6.5). <c>null</c>
    /// fora de <see cref="TransactionKind.Transfer"/>.
    /// </summary>
    public Guid? TransferId { get; private set; }
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

    public Guid? CategorizationRuleId { get; private set; }
    public DateTimeOffset? CategorizedAt { get; private set; }

    public Guid? RecurringRuleId { get; private set; }

    /// <summary>
    /// Valor com sinal para somas de saldo: positivo à entrada, negativo à saída.
    /// </summary>
    public decimal SignedAmount
        => Direction == TransactionDirection.Inflow ? Amount.Amount : -Amount.Amount;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    /// <summary>
    /// Transação regular categorizada; a direção segue o tipo da categoria.
    /// </summary>
    public static Transaction CreateRegular(
        Guid accountId,
        Guid categoryId,
        CategoryKind categoryKind,
        DateTimeOffset occurredAt,
        Money amount,
        string? description,
        IEnumerable<string>? tags,
        TenantId tenantId,
        ExchangeRateSnapshot? exchangeRate = null,
        DateTimeOffset? now = null,
        Guid? recurringRuleId = null)
    {
        EnsureCategory(categoryId);

        return Build(
            accountId, categoryId, DirectionFor(categoryKind), TransactionKind.Regular, null,
            occurredAt, amount, description, tags, tenantId, exchangeRate, now, recurringRuleId);
    }

    /// <summary>
    /// Transação regular ainda sem categoria (ex.: recorrente sem categoria).
    /// Conta nos totais pela direção dada.
    /// </summary>
    public static Transaction CreateUncategorized(
        Guid accountId,
        TransactionDirection direction,
        DateTimeOffset occurredAt,
        Money amount,
        string? description,
        IEnumerable<string>? tags,
        TenantId tenantId,
        ExchangeRateSnapshot? exchangeRate = null,
        DateTimeOffset? now = null,
        Guid? recurringRuleId = null)
        => Build(
            accountId, null, direction, TransactionKind.Regular, null,
            occurredAt, amount, description, tags, tenantId, exchangeRate, now, recurringRuleId);

    /// <summary>
    /// Perna de uma transferência entre contas do tenant. Sem categoria, sem
    /// tags, sem regra recorrente. TransferId liga as duas pernas.
    /// </summary>
    public static Transaction CreateTransferLeg(
        Guid accountId,
        Guid transferId,
        TransactionDirection direction,
        DateTimeOffset occurredAt,
        Money amount,
        string? description,
        TenantId tenantId,
        ExchangeRateSnapshot? exchangeRate = null,
        DateTimeOffset? now = null)
    {
        if (transferId == Guid.Empty)
        {
            throw new ArgumentException("TransferId obrigatório.", nameof(transferId));
        }

        return Build(
            accountId, null, direction, TransactionKind.Transfer, transferId,
            occurredAt, amount, description, null, tenantId, exchangeRate, now, null);
    }

    /// <summary>
    /// Acerto de saldo (reconciliação). Sem categoria, sem tags, sem TransferId.
    /// Conta no saldo, não nos totais de receita/despesa.
    /// </summary>
    public static Transaction CreateAdjustment(
        Guid accountId,
        TransactionDirection direction,
        DateTimeOffset occurredAt,
        Money amount,
        string? description,
        TenantId tenantId,
        ExchangeRateSnapshot? exchangeRate = null,
        DateTimeOffset? now = null)
        => Build(
            accountId, null, direction, TransactionKind.Adjustment, null,
            occurredAt, amount, description, null, tenantId, exchangeRate, now, null);

    public static TransactionDirection DirectionFor(CategoryKind kind)
        => kind == CategoryKind.Income ? TransactionDirection.Inflow : TransactionDirection.Outflow;

    private static Transaction Build(
        Guid accountId,
        Guid? categoryId,
        TransactionDirection direction,
        TransactionKind kind,
        Guid? transferId,
        DateTimeOffset occurredAt,
        Money amount,
        string? description,
        IEnumerable<string>? tags,
        TenantId tenantId,
        ExchangeRateSnapshot? exchangeRate,
        DateTimeOffset? now,
        Guid? recurringRuleId)
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
            Direction = direction,
            Kind = kind,
            TransferId = transferId,
            OccurredAt = occurredAt,
            Amount = amount,
            Description = normalized,
            ExchangeRateToPrimary = exchangeRate?.Rate,
            ExchangeRateAt = exchangeRate?.At,
            RecurringRuleId = recurringRuleId,
        };
        transaction._tags.AddRange(normalizedTags);
        return transaction;
    }

    public void Update(
        Guid accountId,
        Guid categoryId,
        CategoryKind categoryKind,
        DateTimeOffset occurredAt,
        Money amount,
        string? description,
        IEnumerable<string>? tags,
        DateTimeOffset? now = null)
    {
        EnsureRegular();
        EnsureCategory(categoryId);

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
        Direction = DirectionFor(categoryKind);
        OccurredAt = occurredAt;
        Amount = amount;
        Description = NormalizeDescription(description);
        // ExchangeRateToPrimary / ExchangeRateAt são frozen — não
        // recomputados em Update (decisão registada em
        // requirements.md "Decisions").

        _tags.Clear();
        _tags.AddRange(NormalizeTags(tags));
    }

    /// <summary>
    /// Atualiza uma perna de transferência. Apenas AccountId, OccurredAt, Amount
    /// e Description podem mudar. Direction, CategoryId, Kind e TransferId são
    /// imutáveis.
    /// </summary>
    public void UpdateTransferLeg(
        Guid accountId,
        DateTimeOffset occurredAt,
        Money amount,
        string? description,
        ExchangeRateSnapshot? exchangeRate = null,
        DateTimeOffset? now = null)
    {
        EnsureTransfer();

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
        OccurredAt = occurredAt;
        Amount = amount;
        Description = NormalizeDescription(description);

        // Grupo 3 — findings da revisão final: mudar a conta de uma perna
        // pode mudar a moeda; sem recalcular o câmbio, ExchangeRateToPrimary
        // ficava congelado na moeda antiga (ao contrário de Update(), que
        // nunca deixa a moeda mudar — ver TransactionHandlers.Handle(UpdateTransactionCommand)).
        if (exchangeRate is not null)
        {
            ExchangeRateToPrimary = exchangeRate.Rate;
            ExchangeRateAt = exchangeRate.At;
        }
    }

    public void Archive()
    {
        EnsureArchivable();
        DeletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Arquiva (soft delete) uma perna de transferência. Use Archive() apenas
    /// para transações regulares/acertos.
    /// </summary>
    public void ArchiveTransferLeg()
    {
        EnsureTransfer();
        DeletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCategorizedByRule(Guid ruleId)
    {
        CategorizationRuleId = ruleId;
        CategorizedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Recategoriza uma transação regular; a direção passa a seguir o tipo
    /// da nova categoria (despesa → receita inverte o sinal).
    /// </summary>
    public void SetCategory(Guid categoryId, CategoryKind categoryKind)
    {
        EnsureRegular();
        EnsureCategory(categoryId);

        CategoryId = categoryId;
        Direction = DirectionFor(categoryKind);
    }

    /// <summary>
    /// Converte uma transação regular em perna de transferência. Limpa categoria,
    /// regra de categorização e data da categorização. Direction/Amount/AccountId/
    /// OccurredAt permanecem inalterados.
    /// </summary>
    public void ConvertToTransferLeg(Guid transferId)
    {
        if (transferId == Guid.Empty)
        {
            throw new ArgumentException("TransferId obrigatório.", nameof(transferId));
        }

        EnsureRegular();

        Kind = TransactionKind.Transfer;
        CategoryId = null;
        TransferId = transferId;
        CategorizationRuleId = null;
        CategorizedAt = null;
    }

    private void EnsureRegular()
    {
        if (Kind != TransactionKind.Regular)
        {
            throw new TransactionNotRegularException();
        }
    }

    private void EnsureTransfer()
    {
        if (Kind != TransactionKind.Transfer)
        {
            throw new TransactionNotTransferLegException();
        }
    }

    private void EnsureArchivable()
    {
        if (Kind == TransactionKind.Transfer)
        {
            throw new TransactionIsTransferLegException();
        }
    }

    private static void EnsureCategory(Guid categoryId)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("Categoria obrigatória.", nameof(categoryId));
        }
    }

    public void SetRecurringRuleId(Guid recurringRuleId)
    {
        RecurringRuleId = recurringRuleId;
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
