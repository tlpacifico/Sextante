using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Sextante.Infrastructure.ErrorHandling;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandler(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail, errorType) = MapException(exception);

        var problem = new ProblemDetails
        {
            Type = $"urn:sextante:errors:{errorType}",
            Title = title,
            Status = statusCode,
            Detail = _environment.IsDevelopment() ? detail : GetSafeDetail(exception),
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
        };

        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString()
            ?? httpContext.TraceIdentifier;

        if (exception is ValidationException validationException)
        {
            var errors = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            problem.Extensions["errors"] = errors;
        }

        httpContext.Response.StatusCode = statusCode;

        // WriteAsJsonAsync sobrescreve ContentType para "application/json;
        // charset=utf-8" — passamos o ContentType pelo overload para preservar
        // "application/problem+json" (RFC 7807 §3).
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken);

        return true;
    }

    private static (int StatusCode, string Title, string Detail, string ErrorType) MapException(Exception ex) => ex switch
    {
        ValidationException validationEx => (
            400,
            "Erros de validação",
            validationEx.Message,
            "validation"),

        UnauthorizedAccessException => (
            401,
            "Não autorizado",
            "É necessário autenticação para aceder a este recurso.",
            "unauthorized"),

        KeyNotFoundException or EntityNotFoundException => (
            404,
            "Não encontrado",
            ex.Message,
            "not-found"),

        UniqueConstraintException => (
            409,
            "Conflito",
            ex.Message,
            "conflict"),

        ExchangeRateUnavailableException => (
            422,
            "Taxa de câmbio indisponível",
            ex.Message,
            "exchange-rate-unavailable"),

        _ => (
            500,
            "Erro interno",
            ex.Message,
            "internal"),
    };

    private static string GetSafeDetail(Exception ex)
        => ex switch
        {
            ValidationException => ex.Message,
            UnauthorizedAccessException => ex.Message,
            KeyNotFoundException => ex.Message,
            EntityNotFoundException => ex.Message,
            UniqueConstraintException => ex.Message,
            ExchangeRateUnavailableException => ex.Message,
            _ => "Ocorreu um erro inesperado. Tente novamente mais tarde.",
        };
}

public sealed class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entityName, Guid id)
        : base($"{entityName} com ID '{id}' não encontrado(a).") { }

    public EntityNotFoundException(string entityName)
        : base($"{entityName} não encontrado(a).") { }
}

public sealed class UniqueConstraintException : Exception
{
    public UniqueConstraintException(string message) : base(message) { }
}

public sealed class ExchangeRateUnavailableException : Exception
{
    public ExchangeRateUnavailableException(string from, string to, DateTimeOffset date)
        : base($"Taxa de câmbio {from} → {to} indisponível para {date:yyyy-MM-dd}.") { }
}
