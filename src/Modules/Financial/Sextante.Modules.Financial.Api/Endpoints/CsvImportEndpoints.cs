using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Application.Features.CsvImport;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Domain.ImportBatches;
using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

public static class CsvImportEndpoints
{
    public static IEndpointRouteBuilder MapCsvImportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/imports").RequireAuthorization();

        group.MapPost("/upload", async (
            IFormFile file,
            Guid? importProfileId,
            IImportProfileRepository profileRepo,
            IImportBatchRepository batchRepo,
            IDuplicateDetector duplicateDetector,
            ICategorizationRuleEngine ruleEngine,
            ICsvParser csvParser,
            ITenantContext tenant,
            CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = ["Ficheiro CSV obrigatório."] },
                    statusCode: StatusCodes.Status400BadRequest);
            if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = ["Apenas ficheiros .csv são aceites."] },
                    statusCode: StatusCodes.Status400BadRequest);
            const int maxSize = 5 * 1024 * 1024;
            if (file.Length > maxSize)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = ["O ficheiro excede o tamanho máximo de 5 MB."] },
                    statusCode: StatusCodes.Status400BadRequest);

            try
            {
                using var stream = file.OpenReadStream();
                var response = await CsvImportHandlers.Handle(
                    stream, file.FileName, importProfileId,
                    profileRepo, batchRepo, duplicateDetector, ruleEngine, csvParser, tenant, ct);
                return Results.Ok(response);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["csv"] = [ex.Message] },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).DisableAntiforgery();

        group.MapPut("{batchId:guid}/preview", async (
            Guid batchId,
            UpdatePreviewCommand command,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            var response = await bus.InvokeAsync<UploadCsvResponse?>(
                command with { BatchId = batchId }, ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        });

        group.MapPost("{batchId:guid}/confirm", async (
            Guid batchId,
            ConfirmImportCommand command,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            try
            {
                var response = await bus.InvokeAsync<ImportConfirmResponse>(
                    command with { BatchId = batchId }, ct);
                return Results.Ok(response);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["import"] = [ex.Message] },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["import"] = [ex.Message] },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapGet("", async (IMessageBus bus, CancellationToken ct) =>
        {
            var batches = await bus.InvokeAsync<IReadOnlyList<ImportBatchResponse>>(new ListImportBatchesQuery(), ct);
            return Results.Ok(batches);
        });

        return routes;
    }
}
