namespace Sextante.Modules.Identity.Infrastructure.Email;

/// <summary>
/// Corpos de email em PT-PT (tech-stack §16). Texto e HTML simples — sem
/// imagens nem tracking: o relay externo entrega melhor e não há nada a
/// medir num sistema de um utilizador.
/// </summary>
public static class EmailTemplates
{
    private const string Signature = "— Sextante";

    public static EmailMessage PasswordReset(string to, string link)
        => new(
            to,
            "Sextante — recuperação da palavra-passe",
            $"""
            Recebemos um pedido para redefinir a palavra-passe da sua conta.

            Abra o link seguinte para escolher uma nova palavra-passe:
            {link}

            Se não foi você a pedir, ignore este email — a palavra-passe
            actual continua válida.

            {Signature}
            """,
            Html(
                "Recuperação da palavra-passe",
                "Recebemos um pedido para redefinir a palavra-passe da sua conta.",
                "Redefinir palavra-passe",
                link,
                "Se não foi você a pedir, ignore este email — a palavra-passe actual continua válida."));

    public static EmailMessage PasswordResetCode(string to, string code)
        => new(
            to,
            "Sextante — código de recuperação da palavra-passe",
            $"""
            O seu código para redefinir a palavra-passe é: {code}

            Se não foi você a pedir, ignore este email.

            {Signature}
            """,
            Html(
                "Código de recuperação",
                $"O seu código para redefinir a palavra-passe é <strong>{code}</strong>.",
                null,
                null,
                "Se não foi você a pedir, ignore este email."));

    public static EmailMessage EmailConfirmation(string to, string link)
        => new(
            to,
            "Sextante — confirme o seu email",
            $"""
            Confirme o seu endereço de email para activar todas as
            funcionalidades da sua conta:
            {link}

            {Signature}
            """,
            Html(
                "Confirme o seu email",
                "Confirme o seu endereço de email para activar todas as funcionalidades da sua conta.",
                "Confirmar email",
                link,
                null));

    private static string Html(
        string heading,
        string intro,
        string? buttonLabel,
        string? link,
        string? footer)
    {
        var button = buttonLabel is null || link is null
            ? string.Empty
            : $"""
              <p><a href="{link}" style="background:#0f766e;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none;display:inline-block">{buttonLabel}</a></p>
              <p style="font-size:12px;color:#555">Se o botão não funcionar, copie este endereço: {link}</p>
              """;

        var footerHtml = footer is null
            ? string.Empty
            : $"""<p style="font-size:12px;color:#555">{footer}</p>""";

        return $"""
        <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:14px;color:#111">
          <h2 style="font-size:18px">{heading}</h2>
          <p>{intro}</p>
          {button}
          {footerHtml}
          <p style="font-size:12px;color:#555">{Signature}</p>
        </div>
        """;
    }
}
