using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Domain.Enums;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Events;
using Sextante.SharedKernel;
using Wolverine;

namespace Sextante.Modules.Identity.Api.Endpoints;

public sealed record SignupRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, MinLength(12)] string Password,
    [property: Required, MinLength(2), MaxLength(200)] string TenantName);

public sealed record SignupResponse(Guid UserId, Guid TenantId);

public static class SignupEndpoint
{
    public static IEndpointRouteBuilder MapSignup(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/signup", HandleAsync)
            .AllowAnonymous()
            .AddEndpointFilter<DataAnnotationsValidationFilter<SignupRequest>>()
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
        ILogger<SignupRequest> logger,
        CancellationToken ct)
    {
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
            // Não devolver os códigos do Identity (`DuplicateEmail`,
            // `PasswordRequiresDigit`, etc.) — distinguir "email já existe"
            // de "password fraca" é um oráculo de enumeração de utilizadores.
            // Logar server-side para debug; ao cliente devolver mensagem
            // genérica.
            logger.LogInformation(
                "Signup recusado para {Email}: {Errors}",
                request.Email,
                string.Join("; ", createUserResult.Errors.Select(e => $"{e.Code}={e.Description}")));

            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Não foi possível criar a conta com os dados fornecidos."],
            });
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
        //    (sem subscriber). ADR-010 §"Pendentes" #3 e CHANGELOG marcam
        //    o refactor para handler Wolverine + AutoApplyTransactions
        //    (outbox transacional nativa) como deferred para Phase 2.
        await bus.PublishAsync(new UserRegisteredIntegrationEvent(
            user.Id,
            tenant.Id,
            user.Email!,
            DateTimeOffset.UtcNow));

        return Results.Created($"/api/users/{user.Id}", new SignupResponse(user.Id, tenant.Id));
    }
}

/// <summary>
/// Endpoint filter que corre <see cref="Validator.TryValidateObject"/> no
/// argumento do tipo <typeparamref name="T"/> antes de chamar o handler.
/// Minimal endpoints em .NET 10 não validam <c>DataAnnotations</c>
/// automaticamente — sem este filter, <c>[Required]</c>, <c>[MinLength]</c>,
/// <c>[EmailAddress]</c> etc. seriam decorativos.
/// </summary>
internal sealed class DataAnnotationsValidationFilter<T> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var arg = context.Arguments.OfType<T>().FirstOrDefault();
        if (arg is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["Body em falta ou em formato inválido."],
            });
        }

        var results = new List<ValidationResult>();
        var ctx = new ValidationContext(arg);
        if (Validator.TryValidateObject(arg, ctx, results, validateAllProperties: true))
        {
            return await next(context);
        }

        var errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty("request").Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(t => t.Member)
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => t.ErrorMessage ?? "Valor inválido.").ToArray());

        return Results.ValidationProblem(errors);
    }
}
