using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.ImportProfiles;
using Sextante.Modules.Financial.Domain.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

public static class ImportProfilesEndpoints
{
    public static IEndpointRouteBuilder MapImportProfilesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/import-profiles").RequireAuthorization();

        group.MapGet("", async (IMessageBus bus, CancellationToken ct) =>
        {
            var profiles = await bus.InvokeAsync<IReadOnlyList<ImportProfileResponse>>(new ListImportProfilesQuery(), ct);
            return Results.Ok(profiles);
        });

        group.MapGet("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var profile = await bus.InvokeAsync<ImportProfileResponse?>(new GetImportProfileByIdQuery(id), ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        });

        group.MapPost("", async (CreateImportProfileCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var created = await bus.InvokeAsync<ImportProfileResponse>(command, ct);
                return Results.Created($"/api/financial/import-profiles/{created.Id}", created);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["importProfile"] = [ex.Message] },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPut("{id:guid}", async (Guid id, UpdateImportProfileCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            var updated = await bus.InvokeAsync<ImportProfileResponse?>(
                command with { Id = id }, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var archived = await bus.InvokeAsync<bool>(new ArchiveImportProfileCommand(id), ct);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }
}
