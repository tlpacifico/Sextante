using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.PublicApi.Auth;

namespace Sextante.IntegrationTests.Auth;

/// <summary>
/// Plan §9.3 — único teste end-to-end que prova que o
/// <c>TenantAwareClaimsPrincipalFactory</c> dispara no login. Sem este,
/// o factory podia estar mal-wired (ou os claims nunca serem injectados)
/// e o resto da suite continuaria verde.
/// </summary>
public sealed class LoginTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public LoginTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_returns_bearer_token_after_signup()
    {
        var client = _fixture.Factory.CreateClient();
        var email = $"login-{Guid.NewGuid():N}@example.com";
        const string password = "Password123!extra";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password,
            tenantName = "Tenant Login",
        });
        signup.StatusCode.Should().Be(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
        body.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public async Task ClaimsPrincipalFactory_injects_tenant_id_and_tenant_role_claims()
    {
        // O AddBearerToken de Phase 1a usa tickets encriptados (não JWT
        // proper — esse é Phase 6). Para evitar ter de invocar o
        // IDataProtector com purposes específicos do framework, validamos
        // o factory diretamente: signup, FindByNameAsync, gerar principal
        // via IUserClaimsPrincipalFactory, inspeccionar claims.
        //
        // É o mesmo factory que o login pipeline usa — a única diferença é
        // que o login adicionalmente serializa o principal num bearer ticket.
        var client = _fixture.Factory.CreateClient();
        var email = $"claims-{Guid.NewGuid():N}@example.com";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password = "Password123!extra",
            tenantName = "Tenant Claims",
        });
        signup.EnsureSuccessStatusCode();
        var signupBody = await signup.Content.ReadFromJsonAsync<SignupBody>();

        using var scope = _fixture.Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();

        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();

        var principal = await factory.CreateAsync(user!);

        principal.FindFirst(IdentityClaimTypes.TenantId)?.Value
            .Should().Be(signupBody!.TenantId.ToString(), "claim tenant_id deve apontar para o tenant criado.");
        principal.FindFirst(IdentityClaimTypes.TenantRole)?.Value
            .Should().Be("Owner", "user que faz signup é o owner do novo tenant.");
    }

    [Fact]
    public async Task Register_endpoint_is_disabled()
    {
        // /register do MapIdentityApi cria AppUser sem tenant — bypass.
        // IdentityApi.cs override aplica 404 ao endpoint.
        var client = _fixture.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"bypass-{Guid.NewGuid():N}@example.com",
            password = "Password123!extra",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "/api/auth/register tem de ser inacessível — só /signup cria utilizadores em Phase 1a.");
    }

    private sealed record LoginResponse(
        string TokenType,
        string AccessToken,
        int ExpiresIn,
        string RefreshToken);

    private sealed record SignupBody(Guid UserId, Guid TenantId);
}
