namespace Sextante.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Mensagem já composta, pronta a entregar. Serializada nos argumentos do
/// job Hangfire — fica na base de dados (schema <c>hangfire</c>), nunca em
/// log de texto claro (tech-stack §13).
/// </summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string HtmlBody);
