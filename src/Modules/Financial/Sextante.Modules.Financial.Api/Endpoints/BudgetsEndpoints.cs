using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.Budgets;
using Sextante.Modules.Financial.Domain.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

public static class BudgetsEndpoints
{
    public static IEndpointRouteBuilder MapBudgetsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/budgets").RequireAuthorization();

        group.MapGet("", async (
            [FromQuery] int? year,
            [FromQuery] int? month,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            try
            {
                var budgets = await bus.InvokeAsync<IReadOnlyList<BudgetResponse>>(
                    new ListBudgetsQuery(year, month), ct);
                return Results.Ok(budgets);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
        });

        group.MapGet("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var budget = await bus.InvokeAsync<BudgetResponse?>(
                new GetBudgetByIdQuery(id), ct);
            return budget is null
                ? Results.Problem(
                    detail: "Orçamento não encontrado.",
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(budget);
        });

        group.MapGet("{id:guid}/progress", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var progress = await bus.InvokeAsync<BudgetProgressResponse?>(
                new GetBudgetProgressQuery(id), ct);
            return progress is null
                ? Results.Problem(
                    detail: "Orçamento não encontrado.",
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(progress);
        });

        group.MapPost("", async (CreateBudgetCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var created = await bus.InvokeAsync<BudgetResponse>(command, ct);
                return Results.Created($"/api/financial/budgets/{created.Id}", created);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPut("{id:guid}", async (
            Guid id,
            UpdateBudgetCommand command,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await bus.InvokeAsync<BudgetResponse?>(
                    command with { Id = id }, ct);
                return updated is null
                    ? Results.Problem(
                        detail: "Orçamento não encontrado.",
                        statusCode: StatusCodes.Status404NotFound)
                    : Results.Ok(updated);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var archived = await bus.InvokeAsync<bool>(new ArchiveBudgetCommand(id), ct);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        // Alerts.

        group.MapGet("alerts/active", async (IMessageBus bus, CancellationToken ct) =>
        {
            var alerts = await bus.InvokeAsync<IReadOnlyList<BudgetAlertResponse>>(
                new ListActiveBudgetAlertsQuery(), ct);
            return Results.Ok(alerts);
        });

        group.MapPost("alerts/{id:guid}/acknowledge", async (
            Guid id,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            var ok = await bus.InvokeAsync<bool>(new AcknowledgeBudgetAlertCommand(id), ct);
            return ok ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }

    private static IResult BadRequest(FinancialDomainException ex)
        => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["budget"] = [ex.Message],
            },
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Erros de validação");
}
