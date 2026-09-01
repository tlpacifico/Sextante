using Hangfire;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Sextante.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Entrega efectiva do email. Corre em worker Hangfire, com retry
/// automático. Sem SMTP configurado desiste em silêncio (log
/// <c>Warning</c>) em vez de falhar — mission §4.4.
///
/// Não é <c>TenantAwareJob&lt;T&gt;</c> de propósito: recuperação de
/// palavra-passe e confirmação de email acontecem antes de existir
/// contexto de tenant, e a mensagem já vem composta — o job não toca em
/// dados de nenhum tenant.
/// </summary>
public sealed class SendEmailJob
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SendEmailJob> _logger;

    public SendEmailJob(IOptions<SmtpOptions> options, ILogger<SendEmailJob> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 5)]
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (_options.Host is not { Length: > 0 } host)
        {
            // Sem retry útil: repetir não faz aparecer configuração.
            _logger.LogWarning(
                "SMTP não configurado (SMTP__HOST vazio) — email descartado: {Subject}. "
                + "Configure o relay para activar confirmação de email e recuperação de palavra-passe.",
                message.Subject);
            return;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();

        using var client = new SmtpClient();

        await client.ConnectAsync(
            host,
            _options.Port,
            _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect,
            cancellationToken).ConfigureAwait(false);

        if (_options.Username is { Length: > 0 } username)
        {
            await client
                .AuthenticateAsync(username, _options.Password ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

        // O destinatário não entra no log (tech-stack §13 — sem PII em texto
        // claro); o assunto basta para correlacionar com o job.
        _logger.LogInformation("Email entregue ao relay: {Subject}", message.Subject);
    }
}
