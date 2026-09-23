using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Accounts;

/// <summary>
/// Conta financeira (corrente, poupança, dinheiro, cartão).
/// <c>OpeningBalance</c> é imutável após criação — ajustes posteriores
/// fazem-se via <c>Transaction</c>. Phase 3 introduz <see cref="Currency"/>
/// fixa por conta (não muda após criação): mudar a currency
/// retroativamente quebra coerência com transações já gravadas.
/// </summary>
public sealed class Account : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int NameMaxLength = 200;

    private Account()
    {
        Name = string.Empty;
        OpeningBalance = null!;
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public AccountType Type { get; private set; }
    public string Currency { get; private set; }
    public Money OpeningBalance { get; private set; }
    public DateOnly OpeningBalanceDate { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static Account Create(
        string name,
        AccountType type,
        string currency,
        Money openingBalance,
        TenantId tenantId,
        DateOnly? openingBalanceDate = null)
    {
        var trimmed = ValidateName(name);

        if (!Sextante.SharedKernel.Currency.IsValidCode(currency))
        {
            throw new ArgumentException(
                $"Currency '{currency}' inválido — esperado ISO 4217 (3 letras maiúsculas).",
                nameof(currency));
        }

        if (!string.Equals(openingBalance.Currency, currency, StringComparison.Ordinal))
        {
            throw new AccountCurrencyMismatchException(currency, openingBalance.Currency);
        }

        if (openingBalance.Amount < 0m && type != AccountType.CreditCard)
        {
            throw new OpeningBalanceNegativeException();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var resolvedOpeningBalanceDate = openingBalanceDate ?? today;
        if (resolvedOpeningBalanceDate > today)
        {
            throw new OpeningBalanceDateInFutureException();
        }

        return new Account
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            Name = trimmed,
            Type = type,
            Currency = currency,
            OpeningBalance = openingBalance,
            OpeningBalanceDate = resolvedOpeningBalanceDate,
        };
    }

    public void Rename(string newName)
    {
        Name = ValidateName(newName);
    }

    /// <summary>
    /// Conta com transações ativas não arquiva — o saldo e o histórico
    /// deixariam de ser explicáveis.
    /// </summary>
    /// <exception cref="AccountHasActiveTransactionsException"/>
    public void EnsureCanArchive(int activeTransactionCount)
    {
        if (activeTransactionCount > 0)
        {
            throw new AccountHasActiveTransactionsException(activeTransactionCount);
        }
    }

    public void ChangeType(AccountType newType)
    {
        if (Type == AccountType.CreditCard && newType != AccountType.CreditCard && OpeningBalance.Amount < 0m)
        {
            throw new AccountTypeChangeInvalidException();
        }

        Type = newType;
    }

    public void Archive()
    {
        DeletedAt = DateTimeOffset.UtcNow;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Nome da conta é obrigatório.", nameof(name));
        }

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Nome da conta tem no máximo {NameMaxLength} caracteres.",
                nameof(name));
        }

        return trimmed;
    }
}
