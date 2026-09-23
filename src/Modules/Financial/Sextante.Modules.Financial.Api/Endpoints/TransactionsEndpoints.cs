using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.Transactions;
using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Domain.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

public static class TransactionsEndpoints
{
    public static IEndpointRouteBuilder MapTransactionsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/transactions").RequireAuthorization();

        group.MapGet("", async (
            [FromQuery] DateTimeOffset? dateFrom,
            [FromQuery] DateTimeOffset? dateTo,
            [FromQuery] Guid[]? categoryIds,
            [FromQuery] Guid[]? accountIds,
            [FromQuery] Guid? recurringRuleId,
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            [FromQuery] string? kind,
            [FromQuery] string? descriptionContains,
            [FromQuery] decimal? amountMin,
            [FromQuery] decimal? amountMax,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            var query = new ListTransactionsQuery(
                dateFrom,
                dateTo,
                categoryIds is { Length: > 0 } ? categoryIds.ToList() : null,
                accountIds is { Length: > 0 } ? accountIds.ToList() : null,
                recurringRuleId,
                pageSize,
                cursor,
                kind,
                descriptionContains,
                amountMin,
                amountMax);
            var result = await bus.InvokeAsync<TransactionsPageResponse>(query, ct);
            return Results.Ok(result);
        });

        // Phase 6 — export CSV com os mesmos filtros do list.
        group.MapGet("export", async (
            [FromQuery] DateTimeOffset? dateFrom,
            [FromQuery] DateTimeOffset? dateTo,
            [FromQuery] Guid[]? categoryIds,
            [FromQuery] Guid[]? accountIds,
            [FromQuery] Guid? recurringRuleId,
            [FromQuery] string? kind,
            [FromQuery] string? descriptionContains,
            [FromQuery] decimal? amountMin,
            [FromQuery] decimal? amountMax,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            var query = new ExportTransactionsQuery(
                dateFrom,
                dateTo,
                categoryIds is { Length: > 0 } ? categoryIds.ToList() : null,
                accountIds is { Length: > 0 } ? accountIds.ToList() : null,
                recurringRuleId,
                kind,
                descriptionContains,
                amountMin,
                amountMax);

            var export = await bus.InvokeAsync<ExportTransactionsResponse>(query, ct);

            // BOM explícito: sem ele o Excel PT-PT estraga os acentos.
            var bytes = Encoding.UTF8.GetPreamble()
                .Concat(Encoding.UTF8.GetBytes(export.Csv))
                .ToArray();

            return Results.File(bytes, "text/csv; charset=utf-8", export.FileName);
        });

        group.MapGet("summary", async (
            [FromQuery] DateTimeOffset? dateFrom,
            [FromQuery] DateTimeOffset? dateTo,
            [FromQuery] Guid[]? categoryIds,
            [FromQuery] Guid[]? accountIds,
            [FromQuery] string? viewMode,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            var query = new TransactionSummaryQuery(
                dateFrom,
                dateTo,
                categoryIds is { Length: > 0 } ? categoryIds.ToList() : null,
                accountIds is { Length: > 0 } ? accountIds.ToList() : null,
                viewMode);
            var summary = await bus.InvokeAsync<TransactionSummaryResponse>(query, ct);
            return Results.Ok(summary);
        });

        group.MapGet("by-category", async (
            [FromQuery] DateTimeOffset? dateFrom,
            [FromQuery] DateTimeOffset? dateTo,
            [FromQuery] Guid[]? categoryIds,
            [FromQuery] Guid[]? accountIds,
            [FromQuery] string? kind,
            [FromQuery] string? viewMode,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            var query = new TransactionsByCategoryQuery(
                dateFrom,
                dateTo,
                categoryIds is { Length: > 0 } ? categoryIds.ToList() : null,
                accountIds is { Length: > 0 } ? accountIds.ToList() : null,
                kind ?? "Expense",
                viewMode);
            var rows = await bus.InvokeAsync<IReadOnlyList<TransactionByCategoryResponse>>(query, ct);
            return Results.Ok(rows);
        });

        group.MapGet("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var transaction = await bus.InvokeAsync<TransactionResponse>(new GetTransactionByIdQuery(id), ct);
            return Results.Ok(transaction);
        });

        group.MapPost("", async (CreateTransactionCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            var created = await bus.InvokeAsync<TransactionResponse>(command, ct);
            return Results.Created($"/api/financial/transactions/{created.Id}", created);
        });

        group.MapPut("{id:guid}", async (Guid id, UpdateTransactionBody body, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var updated = await bus.InvokeAsync<TransactionResponse>(
                    new UpdateTransactionCommand(
                        id,
                        body.AccountId,
                        body.CategoryId,
                        body.OccurredAt,
                        body.Amount,
                        body.Description,
                        body.Tags),
                    ct);
                return Results.Ok(updated);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var archived = await bus.InvokeAsync<bool>(new ArchiveTransactionCommand(id), ct);
                return archived ? Results.NoContent() : Results.NotFound();
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
        });

        // Phase 6.5 grupo 3 — converte uma transação Regular numa perna de
        // transferência, ligando a uma contraparte existente ou criando-a.
        group.MapPost("{id:guid}/convert-to-transfer", async (
            Guid id,
            ConvertToTransferBody body,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            try
            {
                var converted = await bus.InvokeAsync<TransferResponse>(
                    new ConvertToTransferCommand(id, body.CounterpartAccountId, body.CounterpartTransactionId),
                    ct);
                return Results.Ok(converted);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
            catch (KeyNotFoundException ex)
            {
                // Grupo 3 — mesma nota de TransfersEndpoints.NotFound: o
                // GlobalExceptionHandler não corre em Development/testes.
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (FluentValidation.ValidationException ex)
            {
                return Results.ValidationProblem(
                    ex.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                    title: "Erros de validação");
            }
        });

        group.MapPatch("recategorize", async (RecategorizeTransactionsCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var result = await bus.InvokeAsync<RecategorizeTransactionsResponse>(command, ct);
                return Results.Ok(result);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
        }).RequireAuthorization();

        return routes;
    }

    public sealed record UpdateTransactionBody(
        Guid AccountId,
        Guid CategoryId,
        DateTimeOffset OccurredAt,
        decimal Amount,
        string? Description,
        IReadOnlyList<string>? Tags);

    public sealed record ConvertToTransferBody(Guid CounterpartAccountId, Guid? CounterpartTransactionId);

    private static IResult BadRequest(FinancialDomainException ex)
        => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["transaction"] = [ex.Message],
            },
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Erros de validação");
}
