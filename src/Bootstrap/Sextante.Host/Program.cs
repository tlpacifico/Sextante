using LettuceEncrypt;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;
using Sextante.Host;

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
        // No TLS provider is wired up — drop https:// endpoints so Kestrel doesn't try to
        // load a missing dev cert. Production with LettuceEncrypt re-enables 443 above.
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

    app.MapOpenApi("/openapi/v1.json");

    app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
        .WithName("Health");

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

namespace Sextante.Host
{
    public sealed record HealthResponse(string Status, DateTimeOffset At);

    public sealed class HostAssemblyMarker;

    public partial class Program;
}
