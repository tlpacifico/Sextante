using Sextante.Modules.Financial.Domain.Common;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Domain.ImportProfiles;

public sealed class ImportProfile : ITenantOwned, IAuditable, IFinancialAggregate
{
    public const int NameMaxLength = 128;

    private readonly List<ColumnMapping> _columnMappings;

    private ImportProfile()
    {
        Name = null!;
        Delimiter = null!;
        DateFormat = null!;
        DecimalSeparator = null!;
        _columnMappings = new();
    }

    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public string Delimiter { get; private set; }
    public bool HasHeaderRow { get; private set; }
    public string DateFormat { get; private set; }
    public string DecimalSeparator { get; private set; }
    public int SkipRows { get; private set; }

    public IReadOnlyList<ColumnMapping> ColumnMappings
    {
        get => _columnMappings;
        private set
        {
            _columnMappings.Clear();
            if (value is not null)
                _columnMappings.AddRange(value);
        }
    }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }

    public static ImportProfile Create(
        string name,
        IReadOnlyList<ColumnMapping> columnMappings,
        TenantId tenantId,
        string delimiter = ";",
        bool hasHeaderRow = true,
        string dateFormat = "dd-MM-yyyy",
        string decimalSeparator = ",",
        int skipRows = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ImportProfileNameRequiredException();
        if (name.Length > NameMaxLength)
            throw new ImportProfileNameTooLongException(NameMaxLength);
        if (columnMappings is null || columnMappings.Count == 0)
            throw new ImportProfileMappingsRequiredException();
        if (delimiter.Length != 1)
            throw new ImportProfileDelimiterInvalidException();
        if (string.IsNullOrEmpty(decimalSeparator) || decimalSeparator.Length != 1)
            throw new ImportProfileDecimalSeparatorInvalidException();
        if (skipRows < 0)
            throw new ImportProfileSkipRowsNegativeException();

        var profile = new ImportProfile
        {
            Id = GuidV7.NewId(),
            TenantId = tenantId,
            Name = name.Trim(),
            Delimiter = delimiter,
            HasHeaderRow = hasHeaderRow,
            DateFormat = dateFormat,
            DecimalSeparator = decimalSeparator,
            SkipRows = skipRows,
        };
        profile._columnMappings.AddRange(columnMappings);
        return profile;
    }

    public void Update(
        string name,
        IReadOnlyList<ColumnMapping> columnMappings,
        string delimiter,
        bool hasHeaderRow,
        string dateFormat,
        string decimalSeparator,
        int skipRows)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ImportProfileNameRequiredException();
        if (name.Length > NameMaxLength)
            throw new ImportProfileNameTooLongException(NameMaxLength);
        if (columnMappings is null || columnMappings.Count == 0)
            throw new ImportProfileMappingsRequiredException();
        if (delimiter.Length != 1)
            throw new ImportProfileDelimiterInvalidException();
        if (string.IsNullOrEmpty(decimalSeparator) || decimalSeparator.Length != 1)
            throw new ImportProfileDecimalSeparatorInvalidException();
        if (skipRows < 0)
            throw new ImportProfileSkipRowsNegativeException();

        Name = name.Trim();
        Delimiter = delimiter;
        HasHeaderRow = hasHeaderRow;
        DateFormat = dateFormat;
        DecimalSeparator = decimalSeparator;
        SkipRows = skipRows;

        _columnMappings.Clear();
        _columnMappings.AddRange(columnMappings);
    }

    public void Archive() => DeletedAt = DateTimeOffset.UtcNow;
}
