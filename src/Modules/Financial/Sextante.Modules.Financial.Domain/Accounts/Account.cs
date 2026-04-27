using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Accounts;

/// <summary>
/// Conta financeira (corrente, poupança, dinheiro, cartão). <c>OpeningBalance</c>
/// é imutável após criação — ajustes posteriores fazem-se via <c>Transaction</c>.
/// </summary>
public sealed class Account : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int NameMaxLength = 200;

    private Account()
    {
        Name = string.Empty;
        OpeningBalance = null!;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public AccountType Type { get; private set; }
    public Money OpeningBalance { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static Account Create(string name, AccountType type, Money openingBalance, TenantId tenantId)
    {
        var trimmed = ValidateName(name);

        if (openingBalance.Amount < 0m)
        {
            throw new OpeningBalanceNegativeException();
        }

        return new Account
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            Name = trimmed,
            Type = type,
            OpeningBalance = openingBalance,
        };
    }

    public void Rename(string newName)
    {
        Name = ValidateName(newName);
    }

    public void ChangeType(AccountType newType)
    {
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
