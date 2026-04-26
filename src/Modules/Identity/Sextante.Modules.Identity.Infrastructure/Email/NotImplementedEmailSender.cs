using Microsoft.AspNetCore.Identity;
using Sextante.Modules.Identity.Domain.Entities;

namespace Sextante.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Stub fail-loud usado em Phase 1a. Phase 1b (UI) ou Phase 6
/// (pré-deploy) liga um <see cref="IEmailSender{TUser}"/> real.
/// </summary>
public sealed class NotImplementedEmailSender : IEmailSender<AppUser>
{
    private const string Message =
        "Email sender not configured in Phase 1a; functional in Phase 1b/Phase 6";

    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink)
        => throw new NotImplementedException(Message);

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink)
        => throw new NotImplementedException(Message);

    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
        => throw new NotImplementedException(Message);
}
