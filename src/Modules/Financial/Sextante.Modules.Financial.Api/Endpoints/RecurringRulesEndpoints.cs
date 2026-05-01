using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.RecurringRules;
using Sextante.Modules.Financial.Domain.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

public static class RecurringRulesEndpoints
{
    public static IEndpointRouteBuilder MapRecurringRulesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/recurring-rules").RequireAuthorization();

        group.MapGet("", async (IMessageBus bus, CancellationToken ct) =>
        {
            var rules = await bus.InvokeAsync<IReadOnlyList<RecurringRuleResponse>>(
                new ListRecurringRulesQuery(), ct);
            return Results.Ok(rules);
        });

        group.MapGet("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var rule = await bus.InvokeAsync<RecurringRuleResponse?>(
                new GetRecurringRuleByIdQuery(id), ct);
            return rule is null
                ? Results.Problem(
                    detail: "Regra recorrente não encontrada.",
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(rule);
        });

        group.MapGet("{id:guid}/upcoming", async (
            Guid id,
            [FromQuery] int? count,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            var dates = await bus.InvokeAsync<IReadOnlyList<DateOnly>>(
                new GetUpcomingOccurrencesQuery(id, count ?? 10), ct);

            if (dates.Count == 0)
            {
                // Pode ser que a regra não exista ou esteja completed.
                // Verifica se existe.
                var rule = await bus.InvokeAsync<RecurringRuleResponse?>(
                    new GetRecurringRuleByIdQuery(id), ct);
                if (rule is null)
                {
                    return Results.Problem(
                        detail: "Regra recorrente não encontrada.",
                        statusCode: StatusCodes.Status404NotFound);
                }
            }

            return Results.Ok(dates);
        });

        group.MapPost("", async (CreateRecurringRuleCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var created = await bus.InvokeAsync<RecurringRuleResponse>(command, ct);
                return Results.Created(
                    $"/api/financial/recurring-rules/{created.Id}", created);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["recurringRule"] = [ex.Message],
                    },
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Erros de validação");
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPut("{id:guid}", async (
            Guid id,
            UpdateRecurringRuleCommand command,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await bus.InvokeAsync<RecurringRuleResponse?>(
                    command with { Id = id }, ct);
                return updated is null
                    ? Results.Problem(
                        detail: "Regra recorrente não encontrada.",
                        statusCode: StatusCodes.Status404NotFound)
                    : Results.Ok(updated);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["recurringRule"] = [ex.Message],
                    },
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Erros de validação");
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var archived = await bus.InvokeAsync<bool>(new ArchiveRecurringRuleCommand(id), ct);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }
}
