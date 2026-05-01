namespace Sextante.Modules.Financial.Domain.ImportProfiles;

public sealed record ColumnMapping(
    string CsvColumnName,
    TransactionField TransactionField,
    string? DefaultValue);
