using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.CategorizationRules;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Application.Features.CsvImport;
using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.Categories;
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
            Guid? accountId,
            IImportProfileRepository profileRepo,
            IImportBatchRepository batchRepo,
            IAccountRepository accountRepo,
            ICategoryRepository categoryRepo,
            IDuplicateDetector duplicateDetector,
            ICategorizationRuleEngine ruleEngine,
            ITransferCounterpartQuery transferQuery,
            ICsvParser csvParser,
            IEnumerable<IStatementConverter> statementConverters,
            StatementOverlapTrimmer overlapTrimmer,
            ITenantContext tenant,
            CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = ["Ficheiro obrigatório."] },
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            var isStatement = StatementFileSniffer.IsStatementFile(file.FileName);
            if (!isStatement && !file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = ["Apenas ficheiros .csv, .xlsx, .pdf e .json são aceites."] },
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            const int maxSize = 5 * 1024 * 1024;
            if (file.Length > maxSize)
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["file"] = ["O ficheiro excede o tamanho máximo de 5 MB."] },
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);

            if (isStatement)
            {
                // XLSX/PDF precisam de stream com posição: o ficheiro (≤ 5 MB) vai para memória.
                using var buffer = new MemoryStream((int)file.Length);
                await file.CopyToAsync(buffer, ct);
                if (!StatementFileSniffer.ContentMatchesExtension(file.FileName, buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16))))
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["file"] = ["O conteúdo do ficheiro não corresponde à extensão."] },
                        title: "Erros de validação",
                        statusCode: StatusCodes.Status400BadRequest);
                buffer.Position = 0;

                try
                {
                    var statementResponse = await StatementUploadService.UploadAsync(
                        buffer, file.FileName, accountId, statementConverters, overlapTrimmer,
                        batchRepo, accountRepo, categoryRepo, ruleEngine, transferQuery, tenant, ct);
                    return Results.Ok(statementResponse);
                }
                catch (FinancialDomainException ex)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["file"] = [ex.Message] },
                        title: "Erros de validação",
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }

            try
            {
                using var stream = file.OpenReadStream();
                var response = await CsvImportHandlers.Handle(
                    stream, file.FileName, importProfileId, accountId,
                    profileRepo, batchRepo, accountRepo, categoryRepo, duplicateDetector, ruleEngine, transferQuery, csvParser, tenant, ct);
                return Results.Ok(response);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["csv"] = [ex.Message] },
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }).DisableAntiforgery();

        group.MapPut("{batchId:guid}/preview", async (
            Guid batchId,
            UpdatePreviewCommand command,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            try
            {
                var response = await bus.InvokeAsync<UploadCsvResponse?>(
                    command with { BatchId = batchId }, ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["import"] = [ex.Message] },
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            }
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
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["import"] = [ex.Message] },
                    title: "Erros de validação",
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
