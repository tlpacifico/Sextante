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

    // ITenantContext.TenantId é re-lido em cada acesso a partir de
    // HttpContext.User.FindFirst(IdentityClaimTypes.TenantId) — não é
    // cached no scoped service. Logo, comparar o valor (Guid) no início
    // e no fim do pipeline detecta uma mutação de User mid-request
    // (cenário típico: middleware/handler que substitui ClaimsPrincipal).
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context);
            return;
        }

        var initialTenantId = TryReadTenantId(context);

        await _next(context);

        if (initialTenantId is null) return;

        var finalTenantId = TryReadTenantId(context);
        if (finalTenantId is null)
        {
            // TenantContext desapareceu (claim stripped) num request que começou
            // autenticado — também é suspeito.
            LogSwitch(context, initialTenantId.Value, finalTenantId: null);
            return;
        }

        if (initialTenantId.Value != finalTenantId.Value)
        {
            LogSwitch(context, initialTenantId.Value, finalTenantId.Value);
        }
    }

    private static Guid? TryReadTenantId(HttpContext context)
    {
        try
        {
            var tenantContext = context.RequestServices.GetService<ITenantContext>();
            return tenantContext?.TenantId.Value;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void LogSwitch(HttpContext context, Guid initial, Guid? finalTenantId)
    {
        var userId = context.User?.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
        var path = $"{context.Request.Method} {context.Request.Path}";

        _logger.LogError(
            "Tenant switched mid-request: {TenantInitial} -> {TenantFinal}, user={UserId}, path={Path}",
            initial, finalTenantId?.ToString() ?? "<missing>", userId, path);
    }
}

public static class TenantSwitchDetectorMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantSwitchDetector(this IApplicationBuilder builder)
        => builder.UseMiddleware<TenantSwitchDetectorMiddleware>();
}
