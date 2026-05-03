using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Sextante.Infrastructure.ErrorHandling;

namespace Sextante.Modules.Financial.Application.Tests.ErrorHandling;

/// <summary>
/// Phase 5.5 — garante o contrato ProblemDetails RFC 7807: type/title/status/
/// detail/instance/traceId, com extension `errors` para ValidationException.
/// Detail genérico em produção, detail real em development.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Validation_exception_maps_to_400_with_errors_extension()
    {
        var failures = new[]
        {
            new ValidationFailure("Amount", "O valor tem de ser maior que zero."),
            new ValidationFailure("CategoryId", "Categoria é obrigatória."),
        };
        var ex = new ValidationException(failures);
        var ctx = NewContext();

        var handled = await Run(ex, ctx, environment: "Development");

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(400);
        ctx.Response.ContentType.Should().Be("application/problem+json");

        var body = ReadBody(ctx);
        body.GetProperty("type").GetString().Should().Be("urn:sextante:errors:validation");
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().Be("Erros de validação");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();

        var errors = body.GetProperty("errors");
        errors.GetProperty("Amount").EnumerateArray().Single().GetString()
            .Should().Be("O valor tem de ser maior que zero.");
    }

    [Fact]
    public async Task Unknown_exception_maps_to_500_with_safe_detail_in_production()
    {
        var ctx = NewContext();
        var ex = new InvalidOperationException("internal db connection string leaked");

        await Run(ex, ctx, environment: "Production");

        ctx.Response.StatusCode.Should().Be(500);

        var body = ReadBody(ctx);
        body.GetProperty("type").GetString().Should().Be("urn:sextante:errors:internal");
        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetProperty("detail").GetString().Should().NotContain("leaked");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Unknown_exception_in_development_includes_real_detail()
    {
        var ctx = NewContext();
        var ex = new InvalidOperationException("really specific debug message");

        await Run(ex, ctx, environment: "Development");

        var body = ReadBody(ctx);
        body.GetProperty("detail").GetString().Should().Be("really specific debug message");
    }

    [Fact]
    public async Task EntityNotFoundException_maps_to_404()
    {
        var ctx = NewContext();
        var ex = new EntityNotFoundException("Account", Guid.Parse("11111111-1111-1111-1111-111111111111"));

        await Run(ex, ctx, environment: "Development");

        ctx.Response.StatusCode.Should().Be(404);
        var body = ReadBody(ctx);
        body.GetProperty("type").GetString().Should().Be("urn:sextante:errors:not-found");
    }

    [Fact]
    public async Task UnauthorizedAccessException_maps_to_401()
    {
        var ctx = NewContext();
        var ex = new UnauthorizedAccessException("missing tenant claim");

        await Run(ex, ctx, environment: "Development");

        ctx.Response.StatusCode.Should().Be(401);
        var body = ReadBody(ctx);
        body.GetProperty("type").GetString().Should().Be("urn:sextante:errors:unauthorized");
    }

    [Fact]
    public async Task UniqueConstraintException_maps_to_409()
    {
        var ctx = NewContext();
        var ex = new UniqueConstraintException("Budget already exists for this period.");

        await Run(ex, ctx, environment: "Development");

        ctx.Response.StatusCode.Should().Be(409);
        var body = ReadBody(ctx);
        body.GetProperty("type").GetString().Should().Be("urn:sextante:errors:conflict");
    }

    private static async Task<bool> Run(Exception ex, HttpContext ctx, string environment)
    {
        var handler = new GlobalExceptionHandler(new TestEnv(environment));
        return await handler.TryHandleAsync(ctx, ex, CancellationToken.None);
    }

    private static HttpContext NewContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/financial/transactions";
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static JsonElement ReadBody(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        using var doc = JsonDocument.Parse(ctx.Response.Body);
        return doc.RootElement.Clone();
    }

    private sealed class TestEnv : IHostEnvironment
    {
        public TestEnv(string name) => EnvironmentName = name;
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Sextante.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
