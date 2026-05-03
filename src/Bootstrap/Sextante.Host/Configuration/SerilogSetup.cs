using Microsoft.Extensions.Hosting;
using Sentry.Serilog;
using Serilog;
using Serilog.Events;
using Sextante.Infrastructure.Logging;

namespace Sextante.Host.Configuration;

internal static class SerilogSetup
{
    private const string ConsoleFileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}";

    public static Serilog.ILogger CreateBootstrapLogger() =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: ConsoleFileTemplate)
            .WriteTo.File(
                path: Path.Combine(AppContext.BaseDirectory, "logs", "sextante-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate: ConsoleFileTemplate)
            .CreateBootstrapLogger();

    /// <summary>
    /// Configura o logger definitivo do Host. Niveis e overrides vêm de
    /// appsettings (`Serilog:MinimumLevel`); os sinks (Console + File) são
    /// adicionados aqui no código para garantir que os logs do Host nunca
    /// desaparecem em silêncio caso o `Serilog` da config não declare
    /// `WriteTo` — armadilha clássica de Serilog.Settings.Configuration.
    /// </summary>
    public static void UseSextanteSerilog(this IHostBuilder host) =>
        host.UseSerilog((context, services, configuration) =>
        {
            // Phase 5.5 — configura PII properties a partir de appsettings
            var piiConfigured = context.Configuration
                .GetSection("Logging:PiiProperties")
                .Get<string[]>();
            if (piiConfigured is { Length: > 0 })
            {
                PiiScrubbingEnricher.SetPiiProperties(
                    new HashSet<string>(piiConfigured, StringComparer.OrdinalIgnoreCase));
            }

            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.With<PiiScrubbingEnricher>()
                .WriteTo.Console(outputTemplate: ConsoleFileTemplate)
                .WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, "logs", "sextante-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    outputTemplate: ConsoleFileTemplate);

            var sentryDsn = context.Configuration["Sentry:Dsn"]
                ?? context.Configuration["SENTRY__DSN"];
            if (!string.IsNullOrWhiteSpace(sentryDsn))
            {
                configuration.WriteTo.Sentry(o =>
                {
                    o.Dsn = sentryDsn;
                    o.MinimumBreadcrumbLevel = LogEventLevel.Information;
                    o.MinimumEventLevel = LogEventLevel.Error;
                });
            }
        });
}
