using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Infrastructure.Logging;

public sealed class TenantSwitchDetectorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantSwitchDetectorMiddleware> _logger;

    public TenantSwitchDetectorMiddleware(RequestDelegate next, ILogger<TenantSwitchDetectorMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        string? initialTenantId = null;
        try
        {
            var tenantContext = context.RequestServices.GetService<ITenantContext>();
            initialTenantId = tenantContext?.TenantId.Value.ToString();
        }
        catch (UnauthorizedAccessException)
        {
            // Request não autenticada.
        }

        await _next(context);

        if (initialTenantId is null) return;

        try
        {
            var tenantContext = context.RequestServices.GetService<ITenantContext>();
            var finalTenantId = tenantContext?.TenantId.Value.ToString();

            if (finalTenantId is not null && !string.Equals(initialTenantId, finalTenantId, StringComparison.Ordinal))
            {
                var userId = context.User?.FindFirst(
                    System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
                var path = $"{context.Request.Method} {context.Request.Path}";

                _logger.LogError(
                    "Tenant switched mid-request: {TenantInitial} -> {TenantFinal}, user={UserId}, path={Path}",
                    initialTenantId, finalTenantId, userId, path);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Tenant context desapareceu — cenário anómalo mas não crítico.
        }
    }
}

public static class TenantSwitchDetectorMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantSwitchDetector(this IApplicationBuilder builder)
        => builder.UseMiddleware<TenantSwitchDetectorMiddleware>();
}
