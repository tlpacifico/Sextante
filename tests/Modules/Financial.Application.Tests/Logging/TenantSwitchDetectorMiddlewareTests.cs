using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sextante.Infrastructure.Logging;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.SharedKernel;

namespace Sextante.Modules.Financial.Application.Tests.Logging;

/// <summary>
/// Phase 5.5 — verifica que o detector de tenant switch dispara quando o
/// claim 'tenant_id' muda mid-request, e fica silencioso em fluxo normal.
/// </summary>
public sealed class TenantSwitchDetectorMiddlewareTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Logs_error_when_tenant_changes_mid_request()
    {
        var logger = new CapturingLogger();
        var initialUser = NewUser(TenantA);
        var afterSwitchUser = NewUser(TenantB);
        var ctx = NewContext(initialUser);

        var middleware = new TenantSwitchDetectorMiddleware(
            next: _ =>
            {
                // Simula um middleware/handler malicioso a substituir o
                // ClaimsPrincipal — o detector tem de apanhar isto.
                ctx.User = afterSwitchUser;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(ctx);

        logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("Tenant switched mid-request");
    }

    [Fact]
    public async Task Stays_silent_when_tenant_does_not_change()
    {
        var logger = new CapturingLogger();
        var ctx = NewContext(NewUser(TenantA));

        var middleware = new TenantSwitchDetectorMiddleware(
            next: _ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(ctx);

        logger.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_run_for_anonymous_endpoints()
    {
        var logger = new CapturingLogger();
        var ctx = NewContext(NewAnonymousUser());
        ctx.SetEndpoint(new Endpoint(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(new AllowAnonymousAttribute()),
            displayName: "anon"));

        var middleware = new TenantSwitchDetectorMiddleware(
            next: _ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(ctx);

        logger.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Logs_error_when_tenant_disappears_mid_request()
    {
        var logger = new CapturingLogger();
        var ctx = NewContext(NewUser(TenantA));

        var middleware = new TenantSwitchDetectorMiddleware(
            next: _ =>
            {
                // ClaimsPrincipal substituído por um sem claim de tenant.
                ctx.User = NewAnonymousUser();
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(ctx);

        logger.Errors.Should().ContainSingle()
            .Which.Should().Contain("Tenant switched mid-request");
    }

    private static HttpContext NewContext(ClaimsPrincipal user)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = user;
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/financial/transactions";

        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(_ => new HttpContextAccessor { HttpContext = ctx });
        services.AddScoped<ITenantContext>(sp =>
            new ClaimReadingTenantContext(sp.GetRequiredService<IHttpContextAccessor>()));
        ctx.RequestServices = services.BuildServiceProvider();
        return ctx;
    }

    private static ClaimsPrincipal NewUser(Guid tenantId)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim("tenant_id", tenantId.ToString()),
        }, authenticationType: "Test"));

    private static ClaimsPrincipal NewAnonymousUser()
        => new(new ClaimsIdentity());

    private sealed class ClaimReadingTenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _accessor;
        public ClaimReadingTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

        public TenantId TenantId
        {
            get
            {
                var http = _accessor.HttpContext
                    ?? throw new UnauthorizedAccessException("no http context");
                if (http.User.Identity?.IsAuthenticated != true)
                {
                    throw new UnauthorizedAccessException("not authenticated");
                }
                var claim = http.User.FindFirst("tenant_id")?.Value
                    ?? throw new UnauthorizedAccessException("missing tenant_id claim");
                return new TenantId(Guid.Parse(claim));
            }
        }
    }

    private sealed class CapturingLogger : ILogger<TenantSwitchDetectorMiddleware>
    {
        public List<string> Errors { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
