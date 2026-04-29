using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.Transactions;
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
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            var query = new ListTransactionsQuery(
                dateFrom,
                dateTo,
                categoryIds is { Length: > 0 } ? categoryIds.ToList() : null,
                accountIds is { Length: > 0 } ? accountIds.ToList() : null,
                pageSize,
                cursor);
            var result = await bus.InvokeAsync<TransactionsPageResponse>(query, ct);
            return Results.Ok(result);
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
            var transaction = await bus.InvokeAsync<TransactionResponse?>(new GetTransactionByIdQuery(id), ct);
            return transaction is null ? Results.NotFound() : Results.Ok(transaction);
        });

        group.MapPost("", async (CreateTransactionCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var created = await bus.InvokeAsync<TransactionResponse>(command, ct);
                return Results.Created($"/api/financial/transactions/{created.Id}", created);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["transaction"] = [ex.Message],
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPut("{id:guid}", async (Guid id, UpdateTransactionBody body, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var updated = await bus.InvokeAsync<TransactionResponse?>(
                    new UpdateTransactionCommand(
                        id,
                        body.AccountId,
                        body.CategoryId,
                        body.OccurredAt,
                        body.Amount,
                        body.Description,
                        body.Tags),
                    ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["transaction"] = [ex.Message],
                    },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var archived = await bus.InvokeAsync<bool>(new ArchiveTransactionCommand(id), ct);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }

    public sealed record UpdateTransactionBody(
        Guid AccountId,
        Guid CategoryId,
        DateTimeOffset OccurredAt,
        decimal Amount,
        string? Description,
        IReadOnlyList<string>? Tags);
}
