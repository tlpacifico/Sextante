using System.Text.RegularExpressions;

namespace Sextante.SharedKernel;

/// <summary>
/// Reference data ISO 4217 partilhada cross-tenant. Vive em SharedKernel
/// (em vez de Identity.Domain) porque é referenciada pelos módulos
/// Identity (<c>Tenant.PrimaryCurrency</c>) e Financial
/// (<c>Account.Currency</c>, <c>Transaction.Amount.Currency</c>); a
/// tabela <c>shared.currencies</c> é gerida pela mesma migration runner
/// do Identity. Soft-delete é via <see cref="IsActive"/>=false (sem
/// <c>DeletedAt</c>) — decisão registada em
/// <c>specs/2026-04-27-phase-3-multi-currency/requirements.md</c>.
/// </summary>
public sealed partial class Currency : IVersioned
{
    public const int CodeLength = 3;

    private Currency()
    {
        Code = string.Empty;
        Name = string.Empty;
        Symbol = string.Empty;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public string Symbol { get; private set; }
    public int MinorUnits { get; private set; }
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }

    public static Currency Create(string code, string name, string symbol, int minorUnits, bool isActive = true)
    {
        ValidateCode(code);
        ValidateName(name);
        ValidateMinorUnits(minorUnits);

        return new Currency
        {
            Code = code,
            Name = name.Trim(),
            Symbol = symbol?.Trim() ?? string.Empty,
            MinorUnits = minorUnits,
            IsActive = isActive,
        };
    }

    public void Update(string name, string symbol, int minorUnits, bool isActive)
    {
        ValidateName(name);
        ValidateMinorUnits(minorUnits);

        Name = name.Trim();
        Symbol = symbol?.Trim() ?? string.Empty;
        MinorUnits = minorUnits;
        IsActive = isActive;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public static bool IsValidCode(string? code)
        => code is not null && CurrencyCodeRegex().IsMatch(code);

    private static void ValidateCode(string code)
    {
        if (!IsValidCode(code))
        {
            throw new ArgumentException(
                $"Código '{code}' inválido — esperado ISO 4217 (3 letras maiúsculas).",
                nameof(code));
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Nome da moeda é obrigatório.", nameof(name));
        }
    }

    private static void ValidateMinorUnits(int minorUnits)
    {
        if (minorUnits is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minorUnits),
                "MinorUnits tem de estar entre 0 e 6.");
        }
    }

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCodeRegex();
}
