namespace Sextante.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Configuração do relay SMTP (tech-stack §12). Vem do <c>.env</c> como
/// <c>SMTP__HOST</c>, <c>SMTP__PORT</c>, <c>SMTP__USERNAME</c>,
/// <c>SMTP__PASSWORD</c>, <c>SMTP__FROM</c>.
///
/// Ausência de configuração não é erro: a app arranca e funciona, apenas
/// não entrega email (mission §4.4 — self-hosted-friendly).
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "SMTP";

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = "sextante@localhost";

    public string FromName { get; set; } = "Sextante";

    /// <summary>STARTTLS na 587; <c>false</c> força TLS implícito (465).</summary>
    public bool UseStartTls { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
