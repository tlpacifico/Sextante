using Microsoft.Extensions.Configuration;
using Sentry;

namespace Sextante.Infrastructure.Sentry;

public static class SentryPiiScrubber
{
    private static HashSet<string> GetPiiProperties(IConfiguration configuration)
    {
        var configured = configuration
            .GetSection("Logging:PiiProperties")
            .Get<string[]>();

        return configured is { Length: > 0 }
            ? new HashSet<string>(configured, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "email", "password", "description", "notes", "amount", "limitAmount"
            };
    }

    public static SentryEvent Scrub(SentryEvent sentryEvent, IConfiguration configuration)
    {
        var piiProperties = GetPiiProperties(configuration);

        // Scrub message
        if (sentryEvent.Message?.Message is { } msg)
        {
            foreach (var prop in piiProperties)
            {
                if (msg.Contains(prop, StringComparison.OrdinalIgnoreCase))
                {
                    sentryEvent.Message.Message = "[redacted]";
                    break;
                }
            }
        }

        return sentryEvent;
    }
}
