using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.InstallmentPlans;
using Sextante.Modules.Financial.Domain.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

/// <summary>Phase 6.5 grupo 6 — planos de prestações de cartões.</summary>
public static class InstallmentPlansEndpoints
{
    public static IEndpointRouteBuilder MapInstallmentPlansEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/installment-plans").RequireAuthorization();

        group.MapGet("", async ([FromQuery] Guid? accountId, IMessageBus bus, CancellationToken ct) =>
        {
            var plans = await bus.InvokeAsync<IReadOnlyList<InstallmentPlanResponse>>(new ListInstallmentPlansQuery(accountId), ct);
            return Results.Ok(plans);
        });

        group.MapGet("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var plan = await bus.InvokeAsync<InstallmentPlanResponse?>(new GetInstallmentPlanQuery(id), ct);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        });

        group.MapPost("", async (CreateInstallmentPlanCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var created = await bus.InvokeAsync<InstallmentPlanResponse>(command, ct);
                return Results.Created($"/api/financial/installment-plans/{created.Id}", created);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex);
            }
            catch (FluentValidation.ValidationException ex)
            {
                return BadRequest(ex);
            }
        });

        group.MapPut("{id:guid}", async (Guid id, UpdateInstallmentPlanBody body, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var updated = await bus.InvokeAsync<InstallmentPlanResponse?>(
                    new UpdateInstallmentPlanCommand(
                        id,
                        body.PurchaseTransactionId,
                        body.PurchaseDate,
                        body.Description,
                        body.TotalAmount,
                        body.InstallmentCount,
                        body.InstallmentsAlreadyPaid,
                        body.FirstInstallmentDate,
                        body.AnnualRate),
                    ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (FinancialDomainException ex)
            {
                return BadRequest(ex);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex);
            }
            catch (FluentValidation.ValidationException ex)
            {
                return BadRequest(ex);
            }
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var archived = await bus.InvokeAsync<bool>(new ArchiveInstallmentPlanCommand(id), ct);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }

    public sealed record UpdateInstallmentPlanBody(
        Guid? PurchaseTransactionId,
        DateOnly PurchaseDate,
        string Description,
        decimal TotalAmount,
        int InstallmentCount,
        int InstallmentsAlreadyPaid,
        DateOnly FirstInstallmentDate,
        decimal? AnnualRate);

    private static IResult BadRequest(FinancialDomainException ex)
        => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["installmentPlan"] = [ex.Message],
            },
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Erros de validação");

    // Mesma nota de TransfersEndpoints: em Development/testes o
    // GlobalExceptionHandler não corre, por isso apanha-se aqui.
    private static IResult BadRequest(FluentValidation.ValidationException ex)
        => Results.ValidationProblem(
            ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
            title: "Erros de validação");

    private static IResult NotFound(KeyNotFoundException ex)
        => Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
}
