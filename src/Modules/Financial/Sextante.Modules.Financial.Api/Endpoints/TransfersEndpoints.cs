using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.Transfers;
using Sextante.Modules.Financial.Domain.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

public static class TransfersEndpoints
{
    public static IEndpointRouteBuilder MapTransfersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/transfers").RequireAuthorization();

        group.MapGet("{transferId:guid}", async (Guid transferId, IMessageBus bus, CancellationToken ct) =>
        {
            var transfer = await bus.InvokeAsync<TransferResponse?>(new GetTransferByIdQuery(transferId), ct);
            return transfer is null ? Results.NotFound() : Results.Ok(transfer);
        });

        group.MapPost("", async (CreateTransferCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var created = await bus.InvokeAsync<TransferResponse>(command, ct);
                return Results.Created($"/api/financial/transfers/{created.TransferId}", created);
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

        group.MapPut("{transferId:guid}", async (Guid transferId, UpdateTransferBody body, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var updated = await bus.InvokeAsync<TransferResponse>(
                    new UpdateTransferCommand(
                        transferId,
                        body.FromAccountId,
                        body.ToAccountId,
                        body.OccurredAt,
                        body.AmountOut,
                        body.AmountIn,
                        body.Description),
                    ct);
                return Results.Ok(updated);
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

        group.MapDelete("{transferId:guid}", async (Guid transferId, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                await bus.InvokeAsync<bool>(new DeleteTransferCommand(transferId), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(ex);
            }
        });

        return routes;
    }

    public sealed record UpdateTransferBody(
        Guid FromAccountId,
        Guid ToAccountId,
        DateTimeOffset OccurredAt,
        decimal AmountOut,
        decimal? AmountIn,
        string? Description);

    private static IResult BadRequest(FinancialDomainException ex)
        => Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["transfer"] = [ex.Message],
            },
            detail: ex.Message,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Erros de validação");

    // Grupo 3 — mesma nota de NotFound: a política de validação do Wolverine
    // (FluentValidationMiddleware) lança FluentValidation.ValidationException,
    // que o GlobalExceptionHandler mapeia para 400 — mas só corre em
    // produção; em Development/testes apanha-se aqui.
    private static IResult BadRequest(FluentValidation.ValidationException ex)
        => Results.ValidationProblem(
            ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
            title: "Erros de validação");

    // Grupo 3 — KeyNotFoundException é mapeada globalmente pelo
    // GlobalExceptionHandler em produção, mas em Development/testes o
    // pipeline usa UseDeveloperExceptionPage() em vez de UseExceptionHandler()
    // (Program.cs), pelo que o handler global nunca corre aí. Apanhar aqui
    // também torna o 404 correto independente do ambiente. (Revisão final do
    // grupo 3: era EntityNotFoundException, que forçava Financial.Application
    // a referenciar Sextante.Infrastructure, violando o isolamento de módulos
    // do AGENTS.md §3.2 — KeyNotFoundException é BCL, sem essa dependência.)
    private static IResult NotFound(KeyNotFoundException ex)
        => Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
}
