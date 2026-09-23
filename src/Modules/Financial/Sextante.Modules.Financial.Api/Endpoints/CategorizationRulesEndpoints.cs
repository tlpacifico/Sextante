using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.CategorizationRules;
using Sextante.Modules.Financial.Domain.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

public static class CategorizationRulesEndpoints
{
    public static IEndpointRouteBuilder MapCategorizationRulesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/categorization-rules").RequireAuthorization();

        group.MapGet("", async (IMessageBus bus, CancellationToken ct) =>
        {
            var rules = await bus.InvokeAsync<IReadOnlyList<CategorizationRuleResponse>>(new ListCategorizationRulesQuery(), ct);
            return Results.Ok(rules);
        });

        group.MapGet("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var rule = await bus.InvokeAsync<CategorizationRuleResponse?>(new GetCategorizationRuleByIdQuery(id), ct);
            return rule is null ? Results.NotFound() : Results.Ok(rule);
        });

        group.MapPost("", async (CreateCategorizationRuleCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var created = await bus.InvokeAsync<CategorizationRuleResponse>(command, ct);
                return Results.Created($"/api/financial/categorization-rules/{created.Id}", created);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (FluentValidation.ValidationException ex)
            {
                // Em Development/testes o GlobalExceptionHandler não corre
                // (mesma nota de TransfersEndpoints).
                return Results.ValidationProblem(
                    ex.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                    title: "Erros de validação");
            }
        });

        group.MapPut("{id:guid}", async (Guid id, UpdateCategorizationRuleCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var updated = await bus.InvokeAsync<CategorizationRuleResponse?>(
                    command with { Id = id }, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (FluentValidation.ValidationException ex)
            {
                // Em Development/testes o GlobalExceptionHandler não corre
                // (mesma nota de TransfersEndpoints).
                return Results.ValidationProblem(
                    ex.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                    title: "Erros de validação");
            }
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var archived = await bus.InvokeAsync<bool>(new ArchiveCategorizationRuleCommand(id), ct);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        group.MapPut("/reorder", async (ReorderCategorizationRulesCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            await bus.InvokeAsync<bool>(command, ct);
            return Results.Ok();
        });

        group.MapPost("/reapply", async (
            Guid? categoryId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? onlyUncategorized,
            IMessageBus bus,
            CancellationToken ct) =>
        {
            var response = await bus.InvokeAsync<ReapplyCategorizationRulesResponse>(
                new ReapplyCategorizationRulesCommand(
                    categoryId,
                    from,
                    to,
                    onlyUncategorized ?? true), ct);
            return Results.Ok(response);
        });

        return routes;
    }

    private static IResult BadRequest(FinancialDomainException ex)
        => Results.ValidationProblem(
            new Dictionary<string, string[]> { ["categorizationRule"] = [ex.Message] },
            detail: ex.Message,
            title: "Erros de validação",
            statusCode: StatusCodes.Status400BadRequest);
}
