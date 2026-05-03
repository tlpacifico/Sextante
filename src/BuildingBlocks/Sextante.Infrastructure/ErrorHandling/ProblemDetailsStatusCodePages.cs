using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Sextante.Infrastructure.ErrorHandling;

/// <summary>
/// Phase 5.5 — Substitui <c>app.UseStatusCodePages()</c> default e converte
/// códigos 4xx/5xx em ProblemDetails RFC 7807, mesmo quando o endpoint devolve
/// <c>Results.NotFound()</c> em vez de lançar excepção.
/// </summary>
public static class ProblemDetailsStatusCodePages
{
    public static IApplicationBuilder UseProblemDetailsStatusCodePages(this IApplicationBuilder builder)
    {
        return builder.UseStatusCodePages(async context =>
        {
            var statusCode = context.HttpContext.Response.StatusCode;
            if (statusCode < 400) return;

            var traceId = Activity.Current?.TraceId.ToString()
                ?? context.HttpContext.TraceIdentifier;

            var problem = new ProblemDetails
            {
                Type = $"urn:sextante:errors:{MapCode(statusCode)}",
                Title = MapTitle(statusCode),
                Status = statusCode,
                Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}",
            };
            problem.Extensions["traceId"] = traceId;

            context.HttpContext.Response.ContentType = "application/problem+json";
            await context.HttpContext.Response.WriteAsJsonAsync(problem);
        });
    }

    private static string MapCode(int statusCode) => statusCode switch
    {
        400 => "validation",
        401 => "unauthorized",
        403 => "forbidden",
        404 => "not-found",
        409 => "conflict",
        422 => "exchange-rate-unavailable",
        429 => "rate-limited",
        _ => "internal",
    };

    private static string MapTitle(int statusCode) => statusCode switch
    {
        400 => "Erros de validação",
        401 => "Não autorizado",
        403 => "Proibido",
        404 => "Não encontrado",
        409 => "Conflito",
        422 => "Taxa de câmbio indisponível",
        429 => "Demasiados pedidos",
        _ => "Erro interno",
    };
}
