using System.Text.RegularExpressions;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.Categories;

public sealed partial class Category : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int NameMaxLength = 100;

    private Category()
    {
        Name = string.Empty;
        IconName = string.Empty;
        ColorHex = string.Empty;
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public CategoryKind Kind { get; private set; }
    public string IconName { get; private set; }
    public string ColorHex { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static Category Create(
        string name,
        CategoryKind kind,
        string iconName,
        string colorHex,
        TenantId tenantId)
    {
        return new Category
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            Name = ValidateName(name),
            Kind = kind,
            IconName = ValidateIcon(iconName),
            ColorHex = ValidateColor(colorHex),
        };
    }

    public void Update(string name, string iconName, string colorHex)
    {
        Name = ValidateName(name);
        IconName = ValidateIcon(iconName);
        ColorHex = ValidateColor(colorHex);
    }

    /// <summary>
    /// Aplica a regra de domínio "categoria com transações ativas não arquiva".
    /// </summary>
    /// <exception cref="CategoryHasActiveTransactionsException"/>
    public void EnsureCanArchive(int activeTransactionCount)
    {
        if (activeTransactionCount > 0)
        {
            throw new CategoryHasActiveTransactionsException(activeTransactionCount);
        }
    }

    public void Archive()
    {
        DeletedAt = DateTimeOffset.UtcNow;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Nome da categoria é obrigatório.", nameof(name));
        }

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Nome da categoria tem no máximo {NameMaxLength} caracteres.",
                nameof(name));
        }

        return trimmed;
    }

    private static string ValidateIcon(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            throw new InvalidIconNameException(iconName ?? string.Empty);
        }

        var trimmed = iconName.Trim();
        if (!AllowedCategoryIcons.All.Contains(trimmed))
        {
            throw new InvalidIconNameException(trimmed);
        }

        return trimmed;
    }

    private static string ValidateColor(string colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex) || !ColorHexRegex().IsMatch(colorHex))
        {
            throw new InvalidColorHexException(colorHex ?? string.Empty);
        }

        return colorHex;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorHexRegex();
}
