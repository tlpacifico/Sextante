using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.IntegrationTests.Financial;
using Sextante.Modules.Identity.Infrastructure.Email;

namespace Sextante.IntegrationTests.Auth;

/// <summary>
/// Phase 6 (grupo 2) — o <c>NotImplementedEmailSender</c> da Phase 1a foi
/// substituído por envio real via relay SMTP. Duas garantias: a entrega é
/// diferida para um job Hangfire (não bloqueia o request nem o faz falhar),
/// e sem SMTP configurado a app continua a funcionar (mission §4.4).
/// </summary>
public sealed class EmailSendingTests : IClassFixture<IdentityIntegrationFixture>
{
    private readonly IdentityIntegrationFixture _fixture;

    public EmailSendingTests(IdentityIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Password_reset_link_is_queued_and_does_not_throw_without_smtp()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender<AppUser>>();

        var before = await CountQueuedEmailJobs();

        var act = async () => await sender.SendPasswordResetLinkAsync(
            new AppUser { Email = "utilizador@example.com" },
            "utilizador@example.com",
            "https://sextante.example.pt/reset-password?code=abc");

        await act.Should().NotThrowAsync(
            "sem SMTP configurado a app degrada graciosamente (mission §4.4)");

        var after = await CountQueuedEmailJobs();
        after.Should().Be(before + 1, "a entrega é diferida para um job Hangfire");
    }

    [Fact]
    public async Task Confirmation_link_is_queued_too()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender<AppUser>>();

        var before = await CountQueuedEmailJobs();

        await sender.SendConfirmationLinkAsync(
            new AppUser { Email = "novo@example.com" },
            "novo@example.com",
            "https://sextante.example.pt/confirm?code=xyz");

        (await CountQueuedEmailJobs()).Should().Be(before + 1);
    }

    [Fact]
    public void Password_reset_template_is_pt_pt_and_carries_the_link()
    {
        const string link = "https://sextante.example.pt/reset-password?code=abc";

        var message = EmailTemplates.PasswordReset("utilizador@example.com", link);

        message.To.Should().Be("utilizador@example.com");
        message.Subject.Should().ContainEquivalentOf("palavra-passe");
        message.TextBody.Should().Contain(link);
        message.HtmlBody.Should().Contain(link);
    }

    [Fact]
    public void Confirmation_template_is_pt_pt_and_carries_the_link()
    {
        const string link = "https://sextante.example.pt/confirm?code=xyz";

        var message = EmailTemplates.EmailConfirmation("novo@example.com", link);

        message.Subject.Should().ContainEquivalentOf("confirme");
        message.TextBody.Should().Contain(link);
    }

    [Fact]
    public async Task Me_exposes_whether_the_email_is_confirmed()
    {
        var (client, _, userId) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "confirm-flag");

        // Lido do JSON cru de propósito: desserializar para um record com
        // bool deixava a propriedade a false por omissão se o endpoint não a
        // devolvesse, e o teste passava sem testar nada.
        using var fresh = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
        fresh.RootElement.TryGetProperty("emailConfirmed", out var flag)
            .Should().BeTrue("o banner do app-shell depende deste campo");
        flag.GetBoolean().Should().BeFalse("o signup não confirma o email");

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            var token = await users.GenerateEmailConfirmationTokenAsync(user!);
            (await users.ConfirmEmailAsync(user!, token)).Succeeded.Should().BeTrue();
        }

        using var confirmed = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
        confirmed.RootElement.GetProperty("emailConfirmed").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Resend_confirmation_queues_an_email()
    {
        var (client, _, _) = await FinancialTestHelpers.SignupAndLoginAsync(_fixture, "resend");
        var me = await client.GetFromJsonAsync<MeRow>("/api/auth/me");

        var before = await CountQueuedEmailJobs();

        var response = await client.PostAsJsonAsync(
            "/api/auth/resendConfirmationEmail",
            new { email = me!.Email });

        response.IsSuccessStatusCode.Should().BeTrue();
        (await CountQueuedEmailJobs()).Should().Be(before + 1);
    }

    private sealed record MeRow(Guid UserId, string Email);

    private async Task<long> CountQueuedEmailJobs()
    {
        await using var conn = new NpgsqlConnection(_fixture.SuperuserConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM hangfire.job WHERE invocationdata::text LIKE '%SendEmailJob%'";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
