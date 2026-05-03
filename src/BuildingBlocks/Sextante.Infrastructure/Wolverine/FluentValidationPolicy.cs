using Microsoft.Extensions.Logging;
using Wolverine;

namespace Sextante.Infrastructure.Wolverine;

/// <summary>
/// Phase 5.5 — FluentValidation centralizada. Wolverine convention middleware:
/// método <c>Before</c> é descoberto pelo code generator e inserido antes
/// de cada handler. Usa DI para resolver IValidator&lt;T&gt;. Se o validator
/// rejeitar → ValidationException → GlobalExceptionHandler → ProblemDetails 400.
/// </summary>
public sealed class FluentValidationMiddleware
{
    public async Task Before(IMessageContext context, IServiceProvider services, CancellationToken cancellation)
    {
        var message = context.Envelope?.Message;
        if (message is null) return;

        var validatorType = typeof(FluentValidation.IValidator<>).MakeGenericType(message.GetType());
        if (services.GetService(validatorType) is not { } validator) return;

        var validateMethod = validatorType.GetMethod("ValidateAsync",
            [message.GetType(), typeof(CancellationToken)]);
        if (validateMethod is null) return;

        var resultTask = (Task<FluentValidation.Results.ValidationResult>)validateMethod.Invoke(
            validator, [message, cancellation])!;
        var result = await resultTask;

        if (!result.IsValid)
        {
            throw new FluentValidation.ValidationException(result.Errors);
        }
    }
}

/// <summary>
/// Phase 5.5 — Métricas mínimas de handlers Wolverine.
/// Loga duração de cada handler invocation via ILogger.
/// </summary>
public sealed class MetricsMiddleware
{
    public MetricsScope Before(Envelope envelope, ILogger<MetricsMiddleware> logger)
        => new(envelope, logger);

    public sealed class MetricsScope : IDisposable
    {
        private readonly Envelope _envelope;
        private readonly ILogger _logger;
        private readonly long _startedAt;
        private bool _disposed;

        public MetricsScope(Envelope envelope, ILogger logger)
        {
            _envelope = envelope;
            _logger = logger;
            _startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_startedAt);
            var messageType = _envelope.Message?.GetType().Name ?? "unknown";
            _logger.LogInformation(
                "Wolverine handler {HandlerType} completed in {DurationMs}ms",
                messageType, (long)elapsed.TotalMilliseconds);
        }
    }
}
