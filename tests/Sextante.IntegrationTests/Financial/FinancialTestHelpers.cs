using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Sextante.IntegrationTests.Financial;

/// <summary>
/// Helpers para os testes de integração do módulo Financial.
/// Cria signup + login e devolve um <see cref="HttpClient"/> autenticado
/// para o tenant recém-criado.
/// </summary>
internal static class FinancialTestHelpers
{
    public sealed record SignupBody(Guid UserId, Guid TenantId);

    public sealed record LoginBody(
        string TokenType,
        string AccessToken,
        int ExpiresIn,
        string RefreshToken);

    public static async Task<(HttpClient Client, Guid TenantId, Guid UserId)> SignupAndLoginAsync(
        IdentityIntegrationFixture fixture,
        string emailPrefix = "fin")
    {
        var client = fixture.Factory.CreateClient();
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com";
        const string password = "Password123!extra";

        var signupResponse = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            email,
            password,
            tenantName = $"Tenant {emailPrefix}",
        });
        if (!signupResponse.IsSuccessStatusCode)
        {
            var errorBody = await signupResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Signup falhou ({(int)signupResponse.StatusCode}): {errorBody}");
        }
        var signup = await signupResponse.Content.ReadFromJsonAsync<SignupBody>();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginBody>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return (client, signup!.TenantId, signup.UserId);
    }
}
