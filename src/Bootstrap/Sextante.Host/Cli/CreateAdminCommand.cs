using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Domain.Enums;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Events;
using Sextante.SharedKernel;
using Wolverine;

namespace Sextante.Host.Cli;

public sealed class CreateAdminCommand
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        var email = ParseFlag(args, "--email")
            ?? throw new InvalidOperationException("Argumento --email obrigatorio.");
        var password = ParseFlag(args, "--password")
            ?? throw new InvalidOperationException("Argumento --password obrigatorio.");
        var tenantName = ParseFlag(args, "--tenant-name") ?? "Admin Tenant";

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(existing);
            var resetResult = await userManager.ResetPasswordAsync(existing, token, password);
            if (!resetResult.Succeeded)
            {
                Console.Error.WriteLine($"Erro ao rotacionar password: {string.Join(", ", resetResult.Errors.Select(e => e.Description))}");
                return 1;
            }

            var roles = await userManager.GetRolesAsync(existing);
            if (!roles.Contains("Admin"))
            {
                var roleResult = await userManager.AddToRoleAsync(existing, "Admin");
                if (!roleResult.Succeeded)
                {
                    Console.Error.WriteLine($"Erro ao garantir role Admin: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
                    return 1;
                }
            }

            if (!existing.EmailConfirmed)
            {
                existing.EmailConfirmed = true;
                await userManager.UpdateAsync(existing);
            }

            Console.WriteLine($"Admin {email} actualizado (password rotacionada, role garantida).");
            return 0;
        }

        var user = new AppUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            Console.Error.WriteLine($"Erro ao criar utilizador: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
            return 1;
        }

        var addRoleResult = await userManager.AddToRoleAsync(user, "Admin");
        if (!addRoleResult.Succeeded)
        {
            Console.Error.WriteLine($"Erro ao atribuir role Admin: {string.Join(", ", addRoleResult.Errors.Select(e => e.Description))}");
            return 1;
        }

        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Name = tenantName,
        };
        dbContext.Tenants.Add(tenant);

        var membership = new Membership
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TenantId = new TenantId(tenant.Id),
            Role = MembershipRole.Owner,
        };
        dbContext.Memberships.Add(membership);

        await dbContext.SaveChangesAsync();

        await messageBus.PublishAsync(new UserRegisteredIntegrationEvent(
            user.Id,
            tenant.Id,
            user.Email!,
            DateTimeOffset.UtcNow));

        Console.WriteLine($"Admin {email} criado com tenant '{tenantName}'.");
        return 0;
    }

    private static string? ParseFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
