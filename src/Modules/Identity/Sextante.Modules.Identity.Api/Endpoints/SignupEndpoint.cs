using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Domain.Enums;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Events;
using Sextante.SharedKernel;
using Wolverine;

namespace Sextante.Modules.Identity.Api.Endpoints;

public sealed record SignupRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, MinLength(8)] string Password,
    [property: Required, MinLength(2), MaxLength(200)] string TenantName);

public sealed record SignupResponse(Guid UserId, Guid TenantId);

public static class SignupEndpoint
{
    public static IEndpointRouteBuilder MapSignup(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/signup", HandleAsync)
            .AllowAnonymous()
            .WithName("Signup")
            .WithSummary("Cria User + Tenant + Membership(Owner) atomicamente.")
            .Produces<SignupResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> HandleAsync(
        [FromBody] SignupRequest request,
        UserManager<AppUser> userManager,
        IdentityDbContext db,
        IMessageBus bus,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password)
            || string.IsNullOrWhiteSpace(request.TenantName))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["email, password e tenantName são obrigatórios."],
            });
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1. Cria AppUser via Identity.
        var user = new AppUser
        {
            Id = GuidV7.NewId(),
            UserName = request.Email,
            Email = request.Email,
        };
        var createUserResult = await userManager.CreateAsync(user, request.Password);
        if (!createUserResult.Succeeded)
        {
            return Results.ValidationProblem(createUserResult.Errors.ToDictionary(
                e => e.Code,
                e => new[] { e.Description }));
        }

        // 2. Cria Tenant.
        var tenant = new Tenant
        {
            Id = GuidV7.NewId(),
            Name = request.TenantName,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        // 3. Define o tenant ativo na connection — necessário para a policy
        //    RLS WITH CHECK que valida o INSERT em Memberships. set_config(.., true)
        //    limita o scope ao transaction corrente.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, true)",
            new object[] { tenant.Id.ToString() },
            ct);

        // 4. Cria Membership(Owner).
        var membership = new Membership
        {
            Id = GuidV7.NewId(),
            UserId = user.Id,
            TenantId = new TenantId(tenant.Id),
            Role = MembershipRole.Owner,
        };
        db.Memberships.Add(membership);
        await db.SaveChangesAsync(ct);

        // 5. Publica integration event. Storage Wolverine fica no schema messaging
        //    da mesma BD; o INSERT na outbox participa nesta tx via Postgres.
        await bus.PublishAsync(new UserRegisteredIntegrationEvent(
            user.Id,
            tenant.Id,
            user.Email!,
            DateTimeOffset.UtcNow));

        await tx.CommitAsync(ct);

        return Results.Created($"/api/auth/manage/info", new SignupResponse(user.Id, tenant.Id));
    }
}
