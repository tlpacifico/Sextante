using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Sextante.Infrastructure.Logging;

namespace Sextante.Modules.Financial.Application.Tests.Logging;

/// <summary>
/// Phase 5.5 — garante que o enricher PII transforma propriedades sensíveis
/// (email, password, description, notes, amount) antes de chegarem ao sink.
/// O enricher é defesa em profundidade — se um log estruturado incluir
/// directamente um valor PII como propriedade, fica mascarado.
/// </summary>
public sealed class PiiScrubbingEnricherTests
{
    [Fact]
    public void Email_property_is_replaced_with_email_marker()
    {
        var sink = new CapturingSink();
        var logger = BuildLogger(sink);

        logger.Information("user signed in {email}", "tester@example.com");

        var rendered = Render(sink, "email");
        rendered.Should().Contain("[email]");
        rendered.Should().NotContain("tester@example.com");
    }

    [Fact]
    public void Password_property_is_redacted()
    {
        var sink = new CapturingSink();
        var logger = BuildLogger(sink);

        logger.Information("login attempt with {password}", "SuperSecret123!");

        var rendered = Render(sink, "password");
        rendered.Should().Be("\"[redacted]\"");
        rendered.Should().NotContain("SuperSecret");
    }

    [Fact]
    public void Multiple_pii_properties_are_all_masked_in_one_event()
    {
        var sink = new CapturingSink();
        var logger = BuildLogger(sink);

        logger.Information(
            "transaction created {email} {amount} {description}",
            "owner@example.com",
            150.50m,
            "Compra Continente");

        Render(sink, "email").Should().NotContain("owner@example.com");
        Render(sink, "amount").Should().Be("\"[redacted]\"");
        Render(sink, "description").Should().NotContain("Continente");
    }

    [Fact]
    public void Non_sensitive_properties_pass_through_unchanged()
    {
        var sink = new CapturingSink();
        var logger = BuildLogger(sink);

        logger.Information("budget {tenantId} {percentUsed}",
            Guid.Parse("11111111-1111-1111-1111-111111111111"), 80m);

        Render(sink, "tenantId").Should().Contain("11111111");
        Render(sink, "percentUsed").Should().Be("80");
    }

    private static ILogger BuildLogger(CapturingSink sink)
        => new LoggerConfiguration()
            .Enrich.With<PiiScrubbingEnricher>()
            .WriteTo.Sink(sink)
            .CreateLogger();

    private static string Render(CapturingSink sink, string propertyName)
    {
        sink.Events.Should().NotBeEmpty();
        var evt = sink.Events.Last();
        evt.Properties.Should().ContainKey(propertyName);
        return evt.Properties[propertyName].ToString();
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
