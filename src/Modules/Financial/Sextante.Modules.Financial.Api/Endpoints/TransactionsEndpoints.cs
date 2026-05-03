using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.Transactions;
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
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var archived = await bus.InvokeAsync<bool>(new ArchiveTransactionCommand(id), ct);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        group.MapPatch("recategorize", async (RecategorizeTransactionsCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            var result = await bus.InvokeAsync<RecategorizeTransactionsResponse>(command, ct);
            return Results.Ok(result);
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
}
