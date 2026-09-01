using Hangfire;
using Microsoft.AspNetCore.Identity;
using Sextante.Modules.Identity.Domain.Entities;

namespace Sextante.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Substitui o <c>NotImplementedEmailSender</c> da Phase 1a. Não entrega
/// nada inline: compõe a mensagem e enfileira um <see cref="SendEmailJob"/>
/// (tech-stack §12 — entrega via Hangfire, não bloqueia o request e tem
/// retry automático). Um relay em baixo nunca faz falhar um signup ou um
/// pedido de recuperação de palavra-passe.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender<AppUser>
{
    private readonly IBackgroundJobClient _jobs;

    public SmtpEmailSender(IBackgroundJobClient jobs)
    {
        _jobs = jobs;
    }

    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink)
        => Enqueue(EmailTemplates.EmailConfirmation(email, confirmationLink));

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink)
        => Enqueue(EmailTemplates.PasswordReset(email, resetLink));

    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
        => Enqueue(EmailTemplates.PasswordResetCode(email, resetCode));

    private Task Enqueue(EmailMessage message)
    {
        _jobs.Enqueue<SendEmailJob>(job => job.SendAsync(message, CancellationToken.None));
        return Task.CompletedTask;
    }
}
