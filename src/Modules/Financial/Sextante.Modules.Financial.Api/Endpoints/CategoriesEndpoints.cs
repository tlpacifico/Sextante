using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Financial.Application.Features.Categories;
using Sextante.Modules.Financial.Domain.Common;
using Wolverine;

namespace Sextante.Modules.Financial.Api.Endpoints;

public static class CategoriesEndpoints
{
    public static IEndpointRouteBuilder MapCategoriesEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/financial/categories").RequireAuthorization();

        group.MapGet("", async (IMessageBus bus, CancellationToken ct) =>
        {
            var categories = await bus.InvokeAsync<IReadOnlyList<CategoryResponse>>(new ListCategoriesQuery(), ct);
            return Results.Ok(categories);
        });

        group.MapGet("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            var category = await bus.InvokeAsync<CategoryResponse?>(new GetCategoryByIdQuery(id), ct);
            return category is null ? Results.NotFound() : Results.Ok(category);
        });

        group.MapPost("", async (CreateCategoryCommand command, IMessageBus bus, CancellationToken ct) =>
        {
            var created = await bus.InvokeAsync<CategoryResponse>(command, ct);
            return Results.Created($"/api/financial/categories/{created.Id}", created);
        });

        group.MapPut("{id:guid}", async (Guid id, UpdateCategoryBody body, IMessageBus bus, CancellationToken ct) =>
        {
            var updated = await bus.InvokeAsync<CategoryResponse?>(
                new UpdateCategoryCommand(id, body.Name, body.IconName, body.ColorHex),
                ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapDelete("{id:guid}", async (Guid id, IMessageBus bus, CancellationToken ct) =>
        {
            try
            {
                var result = await bus.InvokeAsync<ArchiveCategoryResult>(new ArchiveCategoryCommand(id), ct);
                return result switch
                {
                    ArchiveCategoryResult.NotFound => Results.NotFound(),
                    ArchiveCategoryResult.Archived => Results.NoContent(),
                    _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
                };
            }
            catch (CategoryHasActiveTransactionsException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["category"] = ["Não é possível arquivar uma categoria com transações ativas."],
                    },
                    detail: ex.Message,
                    title: "Erros de validação",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        return routes;
    }

    public sealed record UpdateCategoryBody(string Name, string IconName, string ColorHex);
}
