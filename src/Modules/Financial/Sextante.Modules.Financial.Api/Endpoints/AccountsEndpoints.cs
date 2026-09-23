using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.Accounts;
using Sextante.Modules.Financial.Domain.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

public static class AccountsEndpoints
{
    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/accounts").RequireAuthorization();

        group.MapGet("", async (IMessageBus bus, CancellationToken ct) =>
        {
            var accounts = await bus.InvokeAsync<IReadOnlyList<AccountResponse>>(new ListAccountsQuery(), ct);
            return Results.Ok(accounts);
        });

        group.MapGet("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var account = await bus.InvokeAsync<AccountResponse?>(new GetAccountByIdQuery(id), ct);
            return account is null ? Results.NotFound() : Results.Ok(account);
        });

        group.MapGet("{id:guid}/balance", async (Guid id, [FromQuery] DateOnly? at, IMessageBus bus, CancellationToken ct) =>
        {
            var response = await bus.InvokeAsync<AccountBalanceResponse?>(new GetAccountBalanceQuery(id, at), ct);
            return response is null ? Results.NotFound() : Results.Ok(response);
        });

        // Phase 6.5 grupo 5 — vista do cartão de crédito.
        group.MapGet("{id:guid}/credit-card", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var view = await bus.InvokeAsync<CreditCardViewResponse?>(new GetCreditCardViewQuery(id), ct);
                return view is null ? Results.NotFound() : Results.Ok(view);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["account"] = [ex.Message],
                    },
                    detail: ex.Message,
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        // Phase 6.5 grupo 4 — acerto de saldo (reconciliação).
        group.MapPost("{id:guid}/reconcile", async (Guid id, ReconcileAccountBody body, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var response = await bus.InvokeAsync<ReconcileAccountResponse?>(
                    new ReconcileAccountCommand(id, body.Date, body.ActualBalance),
                    ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["reconcile"] = [ex.Message],
                    },
                    detail: ex.Message,
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
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

        group.MapPost("", async (CreateAccountCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var created = await bus.InvokeAsync<AccountResponse>(command, ct);
                return Results.Created($"/api/financial/accounts/{created.Id}", created);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["account"] = [ex.Message],
                    },
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPut("{id:guid}", async (Guid id, UpdateAccountBody body, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var updated = await bus.InvokeAsync<AccountResponse?>(
                    new UpdateAccountCommand(id, body.Name, body.Type, body.CreditCard),
                    ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (FinancialDomainException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["account"] = [ex.Message],
                    },
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var archived = await bus.InvokeAsync<bool>(new ArchiveAccountCommand(id), ct);
                return archived ? Results.NoContent() : Results.NotFound();
            }
            catch (AccountHasActiveTransactionsException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["account"] = ["Não é possível arquivar uma conta com transações ativas."],
                    },
                    detail: ex.Message,
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        return routes;
    }

    public sealed record ReconcileAccountBody(DateOnly Date, decimal ActualBalance);

    public sealed record UpdateAccountBody(
        string Name,
        Sextante.Modules.Financial.Domain.Accounts.AccountType Type,
        CreditCardSettingsInput? CreditCard = null);
}
