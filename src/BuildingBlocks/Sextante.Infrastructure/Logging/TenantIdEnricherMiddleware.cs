using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Serilog.Context;

namespace Sextante.Infrastructure.Logging;

public sealed class TenantIdEnricherMiddleware
{
    private readonly RequestDelegate _next;

    public TenantIdEnricherMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        var tenantContext = context.RequestServices.GetService<ITenantContext>();
        if (tenantContext is not null)
        {
            try
            {
                var tenantId = tenantContext.TenantId.Value.ToString();
                using (LogContext.PushProperty("TenantId", tenantId))
                {
                    await _next(context);
                    return;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Request não autenticada (ex.: health check). Skip.
            }
        }

        await _next(context);
    }
}

public static class TenantIdEnricherMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantIdEnricher(this IApplicationBuilder builder)
        => builder.UseMiddleware<TenantIdEnricherMiddleware>();
}
