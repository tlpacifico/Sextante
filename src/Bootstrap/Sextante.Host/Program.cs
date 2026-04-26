using LettuceEncrypt;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Serilog;
using Serilog.Events;
using Sextante.Host;
using Sextante.Modules.Identity.Api;
using Sextante.Modules.Identity.Application;
using Sextante.Modules.Identity.Application.Middleware;
using Sextante.Modules.Identity.Infrastructure;
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

    // Identity module ----------------------------------------------------------
    builder.Services.AddIdentityInfrastructure(builder.Configuration);

    // Bearer token (encrypted ticket; fica funcional desde Phase 1a).
    // JWT proper (HS256 com chave assinada) é Phase 6 — keepsake é gerado/exigido
    // já agora para forçar a presença da env var em produção.
    EnsureJwtSigningKey(builder.Configuration, builder.Environment);
    builder.Services.AddAuthentication(IdentityConstants.BearerScheme)
        .AddBearerToken(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
            options.RefreshTokenExpiration = TimeSpan.FromDays(7);
        });
    builder.Services.AddAuthorization();

    // Wolverine: mediator in-process + bus inter-módulos com outbox transacional.
    builder.Host.UseWolverine(opts =>
    {
        var migrationConnection = builder.Configuration.GetConnectionString("Migration")
            ?? builder.Configuration["MIGRATION__CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "Connection string 'Migration' não configurada para Wolverine.");

        opts.PersistMessagesWithPostgresql(migrationConnection, schemaName: "messaging");
        opts.UseEntityFrameworkCoreTransactions();
        opts.Policies.AutoApplyTransactions();
        opts.Policies.UseDurableLocalQueues();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
        // Wolverine cria/actualiza o schema messaging.* no startup — JasperFx
        // hosted service corre antes de o servidor aceitar HTTP.
        opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
        opts.Services.AddResourceSetupOnStartup();

        opts.Discovery.IncludeAssembly(typeof(Sextante.Modules.Identity.Application.AssemblyMarker).Assembly);

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
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapOpenApi("/openapi/v1.json");

    app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
        .WithName("Health");

    app.MapIdentityModule();

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

static void EnsureJwtSigningKey(IConfiguration configuration, IHostEnvironment env)
{
    var fromEnv = configuration["JWT:SIGNING_KEY"]
        ?? configuration["JWT__SIGNING_KEY"]
        ?? Environment.GetEnvironmentVariable("JWT__SIGNING_KEY");

    if (!string.IsNullOrWhiteSpace(fromEnv))
    {
        return;
    }

    if (!env.IsDevelopment())
    {
        throw new InvalidOperationException(
            "JWT__SIGNING_KEY ausente em ambiente não-Development. Configura a env var antes de arrancar o Host.");
    }

    var keyPath = Path.Combine(AppContext.BaseDirectory, "dev-jwt-key.bin");
    if (!File.Exists(keyPath))
    {
        var bytes = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        File.WriteAllBytes(keyPath, bytes);
        Log.Warning("JWT__SIGNING_KEY ausente — gerada chave dev em {Path}.", keyPath);
    }
}

namespace Sextante.Host
{
    public sealed record HealthResponse(string Status, DateTimeOffset At);

    public sealed class HostAssemblyMarker;

    public partial class Program;
}
