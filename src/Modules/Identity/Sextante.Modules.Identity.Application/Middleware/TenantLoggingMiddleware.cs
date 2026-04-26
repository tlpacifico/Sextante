using Microsoft.Extensions.Logging;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Wolverine;

namespace Sextante.Modules.Identity.Application.Middleware;

/// <summary>
/// Middleware Wolverine que enriquece logs com <c>tenant_id</c> e
/// <c>correlation_id</c> ao executar handlers (tech-stack §11).
/// Phase 1a só loga; Phase 6 cruza com Serilog request enrichers.
/// </summary>
public sealed class TenantLoggingMiddleware
{
    public static IDisposable? Before(
        ILogger<TenantLoggingMiddleware> logger,
        IMessageContext context,
        ITenantContext? tenantContext)
    {
        var tenantId = TryReadTenant(tenantContext);
        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["tenant_id"] = tenantId,
            ["correlation_id"] = context.CorrelationId,
            ["wolverine_message"] = context.Envelope?.MessageType,
        });
    }

    private static string? TryReadTenant(ITenantContext? tenantContext)
    {
        if (tenantContext is null)
        {
            return null;
        }

        try
        {
            return tenantContext.TenantId.Value.ToString();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
