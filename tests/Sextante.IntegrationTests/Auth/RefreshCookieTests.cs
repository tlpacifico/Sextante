using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace Sextante.IntegrationTests.Auth;

/// <summary>
/// Phase 1b §3.5 — valida o cookie httpOnly que carrega o refresh token.
/// O middleware adicionado em <c>Sextante.Host.RefreshTokenCookieMiddleware</c>
/// transforma o JSON do MapIdentityApi num cookie e aceita o cookie como
/// fallback para o body em <c>/api/auth/refresh</c>.
/// </summary>
public sealed class RefreshCookieTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public RefreshCookieTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_emits_refresh_cookie_with_security_attributes()
    {
        var client = _fixture.Factory.CreateClient();
        var email = $"cookie-attrs-{Guid.NewGuid():N}@example.com";
        const string password = "Password123!extra";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password,
            tenantName = "Tenant Cookie Attrs",
        });
        signup.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        var setCookies = login.Headers
            .Where(h => string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToArray();

        var refreshCookie = setCookies
            .FirstOrDefault(c => c.StartsWith($"{CookieName}=", StringComparison.OrdinalIgnoreCase));
        refreshCookie.Should().NotBeNull("login tem de emitir Set-Cookie para refresh_token.");

        var cookie = refreshCookie!.ToLowerInvariant();
        cookie.Should().Contain("httponly", "cookie tem de ser HttpOnly para esconder de JS.");
        cookie.Should().Contain("samesite=strict",
            "SameSite=Strict para impedir CSRF em /api/auth/refresh.");
        cookie.Should().Contain("path=/api/auth/refresh",
            "path estreito limita superfície de ataque.");
        cookie.Should().Contain("max-age=604800", "Max-Age de 7 dias alinhado a tech-stack §6.");
    }

    [Fact]
    public async Task Refresh_with_cookie_only_returns_new_tokens()
    {
        var (client, refreshCookie) = await SignupAndLogin("refresh-cookie");

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { }),
        };
        refreshRequest.Headers.Add("Cookie", $"{CookieName}={refreshCookie}");

        var refreshResponse = await client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "refresh sem body mas com cookie deve recuperar tokens novos.");

        var body = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var newCookie = ExtractRefreshCookie(refreshResponse);
        newCookie.Should().NotBeNullOrEmpty(
            "refresh deve renovar também o cookie para roll forward do refresh token.");
    }

    [Fact]
    public async Task Refresh_with_invalid_cookie_returns_401()
    {
        var client = _fixture.Factory.CreateClient();
        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { }),
        };
        refreshRequest.Headers.Add("Cookie", $"{CookieName}=token-invalido");

        var refreshResponse = await client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_emits_delete_cookie()
    {
        var client = _fixture.Factory.CreateClient();
        var email = $"logout-cookie-{Guid.NewGuid():N}@example.com";
        const string password = "Password123!extra";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password,
            tenantName = "Tenant Logout",
        });
        signup.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>();
        tokens.Should().NotBeNull();

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { }),
        };
        logoutRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var logoutResponse = await client.SendAsync(logoutRequest);
        logoutResponse.IsSuccessStatusCode.Should().BeTrue(
            $"logout deveria devolver 2xx, retornou {logoutResponse.StatusCode}.");

        var setCookies = logoutResponse.Headers
            .Where(h => string.Equals(h.Key, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .ToArray();

        setCookies.Should().Contain(c => c.StartsWith($"{CookieName}=", StringComparison.OrdinalIgnoreCase),
            "logout deve emitir Set-Cookie para refresh_token.");

        var deleteCookie = setCookies.First(c => c.StartsWith($"{CookieName}=", StringComparison.OrdinalIgnoreCase));
        deleteCookie.Should().Contain("max-age=0", "delete-cookie usa Max-Age=0.");
        deleteCookie.Should().Contain("path=/api/auth/refresh");
        deleteCookie.Should().Contain("samesite=strict", "SameSite continua Strict.");
        deleteCookie.Should().Contain("httponly");
    }

    private async Task<(HttpClient Client, string RefreshCookieValue)> SignupAndLogin(string label)
    {
        var client = _fixture.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

        var email = $"{label}-{Guid.NewGuid():N}@example.com";
        const string password = "Password123!extra";

        var signup = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password,
            tenantName = $"Tenant {label}",
        });
        signup.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        var refreshCookie = ExtractRefreshCookie(login)
            ?? throw new InvalidOperationException("login não devolveu cookie refresh_token.");

        return (client, refreshCookie);
    }

    private static string? ExtractRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        foreach (var raw in cookies)
        {
            var prefix = $"{CookieName}=";
            if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var afterName = raw[prefix.Length..];
            var endIndex = afterName.IndexOf(';');
            var value = endIndex < 0 ? afterName : afterName[..endIndex];
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }

    private const string CookieName = "refresh_token";

    private sealed record TokenResponse(
        string TokenType,
        string AccessToken,
        int ExpiresIn,
        string RefreshToken);
}
