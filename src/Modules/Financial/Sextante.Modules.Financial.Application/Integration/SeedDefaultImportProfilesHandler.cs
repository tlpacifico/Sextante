using Microsoft.Extensions.Logging;
using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.Modules.Identity.PublicApi.Events;
using Sextante.SharedKernel;
using Wolverine.Attributes;

namespace Sextante.Modules.Financial.Application.Integration;

/// <summary>
/// Subscriber Wolverine para <see cref="UserRegisteredIntegrationEvent"/>.
/// Cria o perfil de importação default (ActivoBank/Millennium CSV) para o
/// tenant recém-criado, para que o utilizador possa importar extratos
/// desde o primeiro dia sem configurar mapeamentos manualmente.
/// </summary>
[NonTransactional]
public static class SeedDefaultImportProfilesHandler
{
    public static async Task Handle(
        UserRegisteredIntegrationEvent @event,
        IImportProfileRepository repository,
        ILogger<UserRegisteredIntegrationEvent> logger,
        CancellationToken cancellationToken)
    {
        var tenantId = new TenantId(@event.TenantId);

        var profile = ImportProfile.Create(
            name: "ActivoBank / Millennium CSV",
            columnMappings: new List<ColumnMapping>
            {
                new("Data Valor", TransactionField.Date, null),
                new("Descrição", TransactionField.Description, null),
                new("Valor", TransactionField.Amount, null),
            },
            tenantId: tenantId,
            delimiter: ";",
            hasHeaderRow: true,
            dateFormat: "dd/MM/yyyy",
            decimalSeparator: ",",
            skipRows: 0);

        await repository.AddAsync(profile, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded perfil de importação default '{ProfileName}' para tenant {TenantId}.",
            profile.Name,
            @event.TenantId);
    }
}
