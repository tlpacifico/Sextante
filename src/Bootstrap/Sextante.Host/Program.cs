using System.Threading.RateLimiting;
using FluentValidation;
using Hangfire;
using LettuceEncrypt;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Events;
using Sextante.Host;
using Sextante.Modules.Financial.Api;
using Sextante.Modules.Financial.Infrastructure;
using Sextante.Modules.Identity.Api;
using Sextante.Modules.Identity.Application;
using Sextante.Modules.Identity.Application.Middleware;
using Sextante.Modules.Identity.Infrastructure;
using Sextante.Modules.Identity.Infrastructure.Jobs;
using JasperFx;
using JasperFx.Resources;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(AppContext.BaseDirectory, "logs", "sextante-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    builder.Services.AddOpenApi();
    builder.Services.AddProblemDetails();

    // Enums via wire como strings ("Checking" em vez de 0) — front-end Angular
    // envia/recebe nomes; allowIntegerValues=true (default) mantém os testes
    // de integração existentes que ainda usam `type = 0` a passar.
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

    // Identity module ----------------------------------------------------------
    builder.Services.AddIdentityInfrastructure(builder.Configuration);

    // Financial module ---------------------------------------------------------
    builder.Services.AddFinancialModule(builder.Configuration);

    // FluentValidation discovery (validators dos endpoints admin Phase 3
    // vivem em Identity.Api).
    builder.Services.AddValidatorsFromAssemblyContaining<
        Sextante.Modules.Identity.Api.Endpoints.ManualExchangeRateValidator>();
    builder.Services.AddValidatorsFromAssemblyContaining<
        Sextante.Modules.Financial.Api.Validators.CreateCategorizationRuleValidator>();

    // Bearer token (encrypted ticket; fica funcional desde Phase 1a).
    // JWT proper (HS256 com chave assinada) é Phase 6 — chave é exigida já
    // agora para forçar a presença da env var em todos os ambientes.
    EnsureJwtSigningKey(builder.Configuration);
    builder.Services.AddAuthentication(IdentityConstants.BearerScheme)
        .AddBearerToken(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
            options.RefreshTokenExpiration = TimeSpan.FromDays(7);
        });
    builder.Services.AddAuthorization();

    // Rate limiting nos endpoints /api/auth/* — defesa contra brute-force,
    // password spray e enumeração. Particionado por IP do cliente; janela
    // fixa de 1 minuto. Limit deliberadamente folgado: 30 req/IP/min cobre
    // workflow legítimo (signup → login → refresh) e bloqueia spray.
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(IdentityApi.AuthRateLimitPolicy, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? httpContext.Request.Headers["X-Forwarded-For"].ToString()
                    ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
    });

    // Wolverine: mediator in-process + bus inter-módulos com outbox transacional.
    builder.Host.UseWolverine(opts =>
    {
        var migrationConnection = builder.Configuration.GetConnectionString("Migration")
            ?? builder.Configuration["MIGRATION__CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "Connection string 'Migration' não configurada para Wolverine.");

        opts.PersistMessagesWithPostgresql(migrationConnection, schemaName: "messaging");
        opts.UseEntityFrameworkCoreTransactions();
        // AutoApplyTransactions corre por default em qualquer chain sem
        // [Transactional]/[NonTransactional]. Com FinancialDbContext +
        // IdentityDbContext registados, o EFCorePersistenceFrameProvider
        // não consegue desambiguar — cada handler Wolverine declara
        // [NonTransactional] e chama repository.SaveChangesAsync
        // explicitamente (UoW por repositório). Quando um handler precisar
        // de outbox transacional, troca para [Transactional] e injecta o
        // DbContext concreto como parâmetro do Handle.
        opts.Policies.UseDurableLocalQueues();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
        // Wolverine cria/actualiza o schema messaging.* no startup — JasperFx
        // hosted service corre antes de o servidor aceitar HTTP.
        opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
        opts.Services.AddResourceSetupOnStartup();

        opts.Discovery.IncludeAssembly(typeof(Sextante.Modules.Identity.Application.AssemblyMarker).Assembly);
        opts.Discovery.IncludeAssembly(typeof(Sextante.Modules.Financial.Application.AssemblyMarker).Assembly);

        // Convenção plural: aceitar `*Handlers` (vertical slices agrupam vários
        // handlers numa única classe estática). Wolverine default é `*Handler`.
        opts.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Includes.WithNameSuffix("Handlers");
        });

        opts.Policies.AddMiddleware<TenantLoggingMiddleware>();
    });

    // TLS / LettuceEncrypt -----------------------------------------------------
    var letsEncryptSection = builder.Configuration.GetSection("LettuceEncrypt");
    var letsEncryptDomains = letsEncryptSection.GetSection("DomainNames").Get<string[]>() ?? [];
    var letsEncryptEmail = letsEncryptSection["EmailAddress"];
    var letsEncryptConfigured =
        builder.Environment.IsProduction()
        && letsEncryptDomains.Length > 0
        && !string.IsNullOrWhiteSpace(letsEncryptEmail);

    if (letsEncryptConfigured)
    {
        builder.Services
            .AddLettuceEncrypt(options =>
            {
                options.AcceptTermsOfService = true;
                options.DomainNames = letsEncryptDomains;
                options.EmailAddress = letsEncryptEmail!;
            })
            .PersistDataToDirectory(new DirectoryInfo("/var/letsencrypt-certs"), pfxPassword: null);
    }
    else
    {
        var requestedUrls = builder.Configuration["urls"]
            ?? builder.Configuration["ASPNETCORE_URLS"];

        if (!string.IsNullOrWhiteSpace(requestedUrls))
        {
            var httpOnly = string.Join(
                ';',
                requestedUrls
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(u => !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)));

            if (string.IsNullOrEmpty(httpOnly))
            {
                httpOnly = "http://+:80";
            }

            if (!string.Equals(httpOnly, requestedUrls, StringComparison.Ordinal))
            {
                Log.Warning(
                    "LettuceEncrypt is not configured (LETSENCRYPT__EMAIL and LETSENCRYPT__DOMAINNAME); "
                    + "dropping HTTPS endpoints. Listening on {Urls}.",
                    httpOnly);
                builder.WebHost.UseUrls(httpOnly);
            }
        }
    }

    var app = builder.Build();

    app.UseForwardedHeaders();
    app.UseSerilogRequestLogging();
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler();
    }
    app.UseStatusCodePages();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    // Phase 1b — emite o refresh token como cookie httpOnly e injecta o
    // valor do cookie no body de /api/auth/refresh quando o request não
    // o trouxer. Tem de estar registado depois de UseRouting (implícito)
    // e antes dos endpoints do Identity para wrappar as suas respostas.
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

    // Recurring snapshot ECB às 00:30 UTC. Idempotente — re-runs com o
    // mesmo provider apenas atualizam timestamps.
    RecurringJob.AddOrUpdate<EcbSnapshotJob>(
        EcbSnapshotJob.RecurringJobId,
        job => job.RunAsync(CancellationToken.None),
        "30 0 * * *",
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    if (Directory.Exists(app.Environment.WebRootPath))
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }

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

static void EnsureJwtSigningKey(IConfiguration configuration)
{
    var fromEnv = configuration["JWT:SIGNING_KEY"]
        ?? configuration["JWT__SIGNING_KEY"];

    if (string.IsNullOrWhiteSpace(fromEnv))
    {
        // Sem fallback: ASPNETCORE_ENVIRONMENT pode ser flippado para
        // Development por engano (env var herdada, .env errado), e gerar
        // uma chave random no disco mascararia o problema. Devs locais
        // metem JWT__SIGNING_KEY em .env (ver .env.example) ou via
        // dotnet user-secrets.
        throw new InvalidOperationException(
            "JWT__SIGNING_KEY ausente. Configura a env var antes de arrancar o Host. "
            + "Gera com `openssl rand -base64 64`.");
    }
}

namespace Sextante.Host
{
    public sealed record HealthResponse(string Status, DateTimeOffset At);

    public sealed class HostAssemblyMarker;

    public partial class Program;
}
