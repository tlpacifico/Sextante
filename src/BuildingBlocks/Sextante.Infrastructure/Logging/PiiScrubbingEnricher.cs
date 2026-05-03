using Serilog.Core;
using Serilog.Events;

namespace Sextante.Infrastructure.Logging;

/// <summary>
/// Serilog enricher que mascara propriedades PII (email, password, etc.)
/// em logs estruturados. A lista de propriedades é configurada via
/// <see cref="SetPiiProperties"/> antes da inicialização do logger.
/// </summary>
public sealed class PiiScrubbingEnricher : ILogEventEnricher
{
    private static HashSet<string> _piiProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "password", "description", "notes", "amount", "limitAmount"
    };

    public PiiScrubbingEnricher()
    {
    }

    public static void SetPiiProperties(HashSet<string> properties)
    {
        _piiProperties = properties;
    }

    public static HashSet<string> GetPiiProperties() => _piiProperties;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var properties = logEvent.Properties.ToList();
        foreach (var kvp in properties)
        {
            if (_piiProperties.Contains(kvp.Key))
            {
                CleanProperty(logEvent, kvp.Key);
            }
        }
    }

    private static void CleanProperty(LogEvent logEvent, string key)
    {
        logEvent.RemovePropertyIfPresent(key);

        if (string.Equals(key, "email", StringComparison.OrdinalIgnoreCase))
            logEvent.AddOrUpdateProperty(new LogEventProperty(key, new ScalarValue("[email]")));
        else
            logEvent.AddOrUpdateProperty(new LogEventProperty(key, new ScalarValue("[redacted]")));
    }
}
