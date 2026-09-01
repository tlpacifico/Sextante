using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Infrastructure.Auth;
using Sextante.Modules.Identity.Infrastructure.Email;
using Sextante.Modules.Identity.Infrastructure.ExchangeRates;
using Sextante.Modules.Identity.Infrastructure.Jobs;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

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
        services.AddScoped<ITenantContextSetter>(sp => (TenantContext)sp.GetRequiredService<ITenantContext>());
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
            .AddIdentityCore<AppUser>(opts =>
            {
                // Phase 1a defaults — endurecidos vs. framework defaults para
                // suportar o threat-model multi-tenant SaaS. Referência:
                // sub-agent review HIGH H5 (2026-04-26).
                //
                // Password length 12 (NIST SP 800-63B Memorized Secret §5.1.1.2):
                // sem complexidade obrigatória, mas tamanho compensa entropia.
                opts.Password.RequiredLength = 12;
                // Lockout: 5 tentativas falhadas em 15 min. Brute-force por
                // utilizador conhecido fica capado mesmo sem rate limiter.
                opts.Lockout.MaxFailedAccessAttempts = 5;
                opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                opts.Lockout.AllowedForNewUsers = true;
                // Email único: bloqueia segundo signup com o mesmo email.
                opts.User.RequireUniqueEmail = true;
                // Phase 6: o email já é entregue a sério, mas
                // RequireConfirmedEmail fica deliberadamente false — num
                // sistema de um utilizador, exigir confirmação só cria risco
                // de auto-lockout se o relay falhar. O banner não bloqueante
                // no app-shell é o mecanismo escolhido; revisitar em
                // Replanning se o sistema passar a ter mais utilizadores.
                opts.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddApiEndpoints();

        // Substitui a factory built-in.
        services.Replace(ServiceDescriptor.Scoped<
            IUserClaimsPrincipalFactory<AppUser>,
            TenantAwareClaimsPrincipalFactory>());

        // Phase 6 — email real via relay SMTP. A entrega corre em job
        // Hangfire (tech-stack §12); sem SMTP__HOST configurado o job
        // desiste com Warning e a app continua a funcionar (mission §4.4).
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddSingleton<IEmailSender<AppUser>, SmtpEmailSender>();
        services.AddScoped<SendEmailJob>();

        services.AddHostedService<MigrationRunner>();

        AddExchangeRateInfrastructure(services, configuration, migrationConnection);
        services.AddScoped<ICurrencyDirectory, CurrencyDirectory>();

        return services;
    }

    private static void AddExchangeRateInfrastructure(
        IServiceCollection services,
        IConfiguration configuration,
        string migrationConnectionString)
    {
        var ecbBaseUrl = configuration["ExchangeRates:EcbBaseUrl"]
            ?? "https://www.ecb.europa.eu/";

        services
            .AddHttpClient<EcbCurrencyProvider>(http =>
            {
                http.BaseAddress = new Uri(ecbBaseUrl);
                http.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromSeconds(2);
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

        services.AddScoped<ICurrencyProvider>(sp => sp.GetRequiredService<EcbCurrencyProvider>());
        services.AddScoped<EcbSnapshotJob>();

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(opts =>
                opts.UseNpgsqlConnection(migrationConnectionString)));

        services.AddHangfireServer(opts =>
        {
            opts.SchedulePollingInterval = TimeSpan.FromSeconds(15);
            opts.WorkerCount = 4;
        });
    }
}
