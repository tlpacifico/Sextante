using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Features.ImportProfiles;

[NonTransactional]
public static class ImportProfileHandlers
{
    public static async Task<ImportProfileResponse> Handle(
        CreateImportProfileCommand command,
        IImportProfileRepository repo,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var mappings = command.ColumnMappings
            .Select(m => new ColumnMapping(
                m.CsvColumnName,
                Enum.Parse<TransactionField>(m.TransactionField),
                m.DefaultValue))
            .ToList();

        var profile = ImportProfile.Create(
            command.Name,
            mappings,
            tenant.TenantId,
            command.Delimiter ?? ";",
            command.HasHeaderRow ?? true,
            command.DateFormat ?? "dd-MM-yyyy",
            command.DecimalSeparator ?? ",",
            command.SkipRows ?? 0);

        await repo.AddAsync(profile, ct);
        await repo.SaveChangesAsync(ct);
        return ToResponse(profile);
    }

    public static async Task<ImportProfileResponse?> Handle(
        UpdateImportProfileCommand command,
        IImportProfileRepository repo,
        CancellationToken ct)
    {
        var profile = await repo.GetByIdAsync(command.Id, ct);
        if (profile is null) return null;

        var mappings = command.ColumnMappings
            .Select(m => new ColumnMapping(
                m.CsvColumnName,
                Enum.Parse<TransactionField>(m.TransactionField),
                m.DefaultValue))
            .ToList();

        profile.Update(
            command.Name,
            mappings,
            command.Delimiter,
            command.HasHeaderRow,
            command.DateFormat,
            command.DecimalSeparator,
            command.SkipRows);

        repo.Update(profile);
        await repo.SaveChangesAsync(ct);
        return ToResponse(profile);
    }

    public static async Task<bool> Handle(
        ArchiveImportProfileCommand command,
        IImportProfileRepository repo,
        CancellationToken ct)
    {
        var profile = await repo.GetByIdAsync(command.Id, ct);
        if (profile is null) return false;

        profile.Archive();
        repo.Update(profile);
        await repo.SaveChangesAsync(ct);
        return true;
    }

    public static async Task<ImportProfileResponse?> Handle(
        GetImportProfileByIdQuery query,
        IImportProfileRepository repo,
        CancellationToken ct)
    {
        var profile = await repo.GetByIdAsync(query.Id, ct);
        return profile is null ? null : ToResponse(profile);
    }

    public static async Task<IReadOnlyList<ImportProfileResponse>> Handle(
        ListImportProfilesQuery query,
        IImportProfileRepository repo,
        CancellationToken ct)
    {
        var profiles = await repo.ListAsync(ct);
        return profiles.Select(ToResponse).ToList();
    }

    private static ImportProfileResponse ToResponse(ImportProfile profile)
        => new(
            profile.Id,
            profile.Name,
            profile.ColumnMappings
                .Select(m => new ColumnMappingDto(m.CsvColumnName, m.TransactionField.ToString(), m.DefaultValue))
                .ToList(),
            profile.Delimiter,
            profile.HasHeaderRow,
            profile.DateFormat,
            profile.DecimalSeparator,
            profile.SkipRows,
            profile.CreatedAt,
            profile.UpdatedAt);
}
