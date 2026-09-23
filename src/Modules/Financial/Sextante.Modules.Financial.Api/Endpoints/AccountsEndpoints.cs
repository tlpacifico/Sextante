using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
            var updated = await bus.InvokeAsync<AccountResponse?>(
                new UpdateAccountCommand(id, body.Name, body.Type),
                ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
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

    public sealed record UpdateAccountBody(string Name, Sextante.Modules.Financial.Domain.Accounts.AccountType Type);
}
