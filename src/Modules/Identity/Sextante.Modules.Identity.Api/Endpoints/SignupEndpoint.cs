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

        var tenant = new Tenant
        {
            Id = GuidV7.NewId(),
            Name = request.TenantName,
        };

        // Sobrepõe o GUC sentinel definido no checkout do pool. set_config(.., true)
        // limita o scope a esta transação; o pool-level interceptor reescreve no
        // próximo checkout (defesa contra leak entre tenants).
        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id', {0}, true)",
            new object[] { tenant.Id.ToString() },
            ct);

        // 1. Cria AppUser via Identity. SaveChanges interno junta-se à tx.
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

        // 2. Cria Tenant — RLS WITH CHECK passa porque current_tenant_id == tenant.Id.
        //    Save imediato: a FK SQL Memberships.tenant_id → Tenants.Id não está no
        //    modelo EF (TenantId é value object); sem este flush, EF reordena os
        //    INSERTs e o constraint da DB dispara.
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        // 3. Cria Membership(Owner) e persiste na mesma tx.
        var membership = new Membership
        {
            Id = GuidV7.NewId(),
            UserId = user.Id,
            TenantId = new TenantId(tenant.Id),
            Role = MembershipRole.Owner,
        };
        db.Memberships.Add(membership);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        // 4. Publica integration event pós-commit. Phase 1a: at-most-once
        //    (sem subscriber). Phase 2 (módulo Financial) refactoriza para
        //    handler Wolverine, onde Policies.AutoApplyTransactions garante
        //    outbox transacional nativa.
        await bus.PublishAsync(new UserRegisteredIntegrationEvent(
            user.Id,
            tenant.Id,
            user.Email!,
            DateTimeOffset.UtcNow));

        return Results.Created($"/api/auth/manage/info", new SignupResponse(user.Id, tenant.Id));
    }
}
