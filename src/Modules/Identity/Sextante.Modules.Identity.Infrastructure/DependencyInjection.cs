using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Infrastructure.Auth;
using Sextante.Modules.Identity.Infrastructure.Email;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var migrationConnection = configuration.GetConnectionString("Migration")
            ?? configuration["MIGRATION__CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "Connection string 'Migration' não configurada (ConnectionStrings:Migration ou MIGRATION__CONNECTION_STRING).");

        var appConnection = configuration.GetConnectionString("App")
            ?? configuration["APP__CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "Connection string 'App' não configurada (ConnectionStrings:App ou APP__CONNECTION_STRING).");

        services.AddSingleton(new IdentityMigrationConnectionString(migrationConnection));

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddScoped<TenantPopulationInterceptor>();
        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<IdentityDbContext>((sp, options) =>
        {
            options.UseNpgsql(appConnection, npgsql =>
                    npgsql.MigrationsHistoryTable("__migrations", "shared"))
                .AddInterceptors(
                    sp.GetRequiredService<TenantConnectionInterceptor>(),
                    sp.GetRequiredService<TenantPopulationInterceptor>(),
                    sp.GetRequiredService<AuditingInterceptor>());
        });

        services
            .AddIdentityCore<AppUser>()
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddApiEndpoints();

        // Substitui a factory built-in.
        services.Replace(ServiceDescriptor.Scoped<
            IUserClaimsPrincipalFactory<AppUser>,
            TenantAwareClaimsPrincipalFactory>());

        services.AddSingleton<IEmailSender<AppUser>, NotImplementedEmailSender>();

        services.AddHostedService<MigrationRunner>();

        return services;
    }
}
