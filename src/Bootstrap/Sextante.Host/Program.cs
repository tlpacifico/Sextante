using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Sextante.Host;
using Sextante.Host.Cli;
using Sextante.Host.Configuration;
using Sextante.Infrastructure.ErrorHandling;
using Sextante.Infrastructure.Logging;
using Sextante.Infrastructure.Security;
using Sextante.Infrastructure.Sentry;
using Sextante.Modules.Financial.Api;
using Sextante.Modules.Financial.Infrastructure;
using Sextante.Modules.Identity.Api;
using Sextante.Modules.Identity.Application;
using Sextante.Modules.Identity.Infrastructure;

Log.Logger = SerilogSetup.CreateBootstrapLogger();

try
{
    // Phase 5.5 — CLI mode: o comando create-admin corre antes de o servidor HTTP arrancar.
    // Usa Host.CreateApplicationBuilder para registar Identidade + Financeiro sem Kestrel,
    // executa o comando e sai.
    if (args.Length > 0 && args[0] == "create-admin")
    {
        var cliBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(Program).Assembly.GetName().Name,
        });

        cliBuilder.Services.AddIdentityInfrastructure(cliBuilder.Configuration);
        cliBuilder.Services.AddFinancialModule(cliBuilder.Configuration);

        var cliHost = cliBuilder.Build();
        var exitCode = await CreateAdminCommand.RunAsync(args, cliHost.Services);
        Environment.Exit(exitCode);
    }

    Log.Information("Sextante: building host…");
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    builder.Host.UseSextanteSerilog();

    // Phase 5.5 — Sentry error tracking (fallback graceful: DSN ausente → skip).
    var sentryDsn = builder.Configuration["Sentry:Dsn"]
        ?? builder.Configuration["SENTRY__DSN"];
    if (!string.IsNullOrWhiteSpace(sentryDsn))
    {
        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = sentryDsn;
            options.TracesSampleRate = builder.Configuration.GetValue("Sentry:TracesSampleRate", 0.1);
            options.SendDefaultPii = false;
            options.Environment = builder.Environment.EnvironmentName;
            options.SetBeforeSend(evt =>
            {
                evt = SentryPiiScrubber.Scrub(evt, builder.Configuration);
                // Tag tenant_id via HttpContextAccessor
                var accessor = new HttpContextAccessor();
                var context = accessor.HttpContext;
                if (context is not null)
                {
                    try
                    {
                        var tenantCtx = context.RequestServices
                            .GetService(typeof(Sextante.Modules.Identity.PublicApi.Abstractions.ITenantContext))
                            as Sextante.Modules.Identity.PublicApi.Abstractions.ITenantContext;
                        if (tenantCtx is not null)
                        {
                            evt.SetTag("tenant_id", tenantCtx.TenantId.Value.ToString());
                        }
                    }
                    catch { }
                }
                return evt;
            });
        });
        Log.Information("Sextante: Sentry enabled (DSN configured)");
    }
    else
    {
        Log.Information("Sextante: Sentry disabled (DSN missing)");
    }

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddCors(options =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:4200", "http://localhost", "https://localhost"];
        options.AddDefaultPolicy(policy => policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
    });

    // Enums via wire como strings ("Checking" em vez de 0) — front-end Angular
    // envia/recebe nomes; allowIntegerValues=true (default) mantém os testes
    // de integração existentes que ainda usam `type = 0` a passar.
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

    Log.Information("Sextante: registering Identity module…");
    builder.Services.AddIdentityInfrastructure(builder.Configuration);
    Log.Information("Sextante: registering Financial module…");
    builder.Services.AddFinancialModule(builder.Configuration);

    Log.Information("Sextante: registering FluentValidation validators…");
    builder.Services.AddValidatorsFromAssemblyContaining<
        Sextante.Modules.Identity.Api.Endpoints.ManualExchangeRateValidator>();
    builder.Services.AddValidatorsFromAssemblyContaining<
        Sextante.Modules.Financial.Api.Validators.CreateRecurringRuleValidator>();

    Log.Information("Sextante: registering authentication + rate limiter (writes dev-jwt-key.bin if missing)…");
    builder.AddSextanteAuthentication();
    Log.Information("Sextante: registering Wolverine (assembly scan + handler discovery)…");
    builder.AddSextanteWolverine();
    Log.Information("Sextante: registering Kestrel/TLS settings…");
    builder.AddSextanteTls();

    Log.Information("Sextante: building app (this triggers hosted services like MigrationRunner, Hangfire, Wolverine schema setup)…");
    var app = builder.Build();
    Log.Information("Sextante: app built. Wiring HTTP pipeline…");

    var lifetime = app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
    lifetime.ApplicationStarted.Register(() =>
        Log.Information("Sextante: ApplicationStarted — hosted services done, Kestrel listening."));

    app.UseForwardedHeaders();
    app.UseSerilogRequestLogging();

    // Phase 5.5 — TraceId propagation antes de tudo para que todos os logs
    // do request partilhem o mesmo identificador.
    app.UseTraceId();

    // Phase 5.5 — Security headers antes de qualquer resposta.
    app.UseSecurityHeaders();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler();
    }
    app.UseProblemDetailsStatusCodePages();

    app.UseCors();

    app.UseAuthentication();
    app.UseAuthorization();

    // Phase 5.5 — TenantId enricher corre depois de Auth (precisa do
    // ITenantContext populado pelos claims).
    app.UseTenantIdEnricher();

    // Phase 5.5 — Detector de tenant-switch corre depois do enricher;
    // faz snapshot do TenantId antes dos handlers e compara no fim.
    app.UseTenantSwitchDetector();

    app.UseRateLimiter();

    // Phase 1b — emite o refresh token como cookie httpOnly e injecta o valor
    // do cookie no body de /api/auth/refresh quando o request não o trouxer.
    // Tem de estar registado depois de UseRouting (implícito) e antes dos
    // endpoints do Identity para wrappar as suas respostas.
    app.UseRefreshTokenCookie();

    app.MapOpenApi("/openapi/v1.json");

    app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
        .WithName("Health");

    app.MapIdentityModule();
    app.MapFinancialModule();

    // Hangfire dashboard restrita a System Admin (tech-stack §1).
    app.UseHangfireDashboard("/api/admin/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireSystemAdminFilter()],
    });

    RecurringJobsRegistration.RegisterRecurringJobs();

    if (Directory.Exists(app.Environment.WebRootPath))
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }

    Log.Information("Sextante: pipeline wired. Calling app.Run()…");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

namespace Sextante.Host
{
    public sealed record HealthResponse(string Status, DateTimeOffset At);

    public sealed class HostAssemblyMarker;

    public partial class Program;
}
