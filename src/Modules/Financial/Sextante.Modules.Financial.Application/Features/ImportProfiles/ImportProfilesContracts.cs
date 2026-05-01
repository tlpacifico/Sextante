using Sextante.Modules.Financial.Domain.ImportProfiles;

namespace Sextante.Modules.Financial.Application.Features.ImportProfiles;

public sealed record ImportProfileResponse(
    Guid Id,
    string Name,
    IReadOnlyList<ColumnMappingDto> ColumnMappings,
    string Delimiter,
    bool HasHeaderRow,
    string DateFormat,
    string DecimalSeparator,
    int SkipRows,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ColumnMappingDto(
    string CsvColumnName,
    string TransactionField,
    string? DefaultValue);

public sealed record CreateImportProfileCommand(
    string Name,
    IReadOnlyList<ColumnMappingDto> ColumnMappings,
    string? Delimiter,
    bool? HasHeaderRow,
    string? DateFormat,
    string? DecimalSeparator,
    int? SkipRows);

public sealed record UpdateImportProfileCommand(
    Guid Id,
    string Name,
    IReadOnlyList<ColumnMappingDto> ColumnMappings,
    string Delimiter,
    bool HasHeaderRow,
    string DateFormat,
    string DecimalSeparator,
    int SkipRows);

public sealed record ArchiveImportProfileCommand(Guid Id);

public sealed record GetImportProfileByIdQuery(Guid Id);

public sealed record ListImportProfilesQuery();
