using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Sextante.Infrastructure.Logging;

public sealed class TraceIdMiddleware
{
    private readonly RequestDelegate _next;

    public TraceIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        using (LogContext.PushProperty("TraceId", traceId))
        {
            context.Response.Headers["X-Trace-Id"] = traceId;
            await _next(context);
        }
    }
}

public static class TraceIdMiddlewareExtensions
{
    public static IApplicationBuilder UseTraceId(this IApplicationBuilder builder)
        => builder.UseMiddleware<TraceIdMiddleware>();
}
