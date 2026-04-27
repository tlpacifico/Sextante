using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.Accounts;
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
            var created = await bus.InvokeAsync<AccountResponse>(command, ct);
            return Results.Created($"/api/financial/accounts/{created.Id}", created);
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
            var archived = await bus.InvokeAsync<bool>(new ArchiveAccountCommand(id), ct);
            return archived ? Results.NoContent() : Results.NotFound();
        });

        return routes;
    }

    public sealed record UpdateAccountBody(string Name, Sextante.Modules.Financial.Domain.Accounts.AccountType Type);
}
