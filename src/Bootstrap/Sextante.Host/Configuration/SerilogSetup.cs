using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

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
        host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: ConsoleFileTemplate)
            .WriteTo.File(
                path: Path.Combine(AppContext.BaseDirectory, "logs", "sextante-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate: ConsoleFileTemplate));
}
