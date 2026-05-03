using LettuceEncrypt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Sextante.Host.Configuration;

internal static class KestrelTlsSetup
{
    public static void AddSextanteTls(this WebApplicationBuilder builder)
    {
        var letsEncryptSection = builder.Configuration.GetSection("LettuceEncrypt");
        var letsEncryptDomains = letsEncryptSection.GetSection("DomainNames").Get<string[]>() ?? [];
        var letsEncryptEmail = letsEncryptSection["EmailAddress"];

        var letsEncryptConfigured =
            builder.Environment.IsProduction()
            && letsEncryptDomains.Length > 0
            && !string.IsNullOrWhiteSpace(letsEncryptEmail);

        if (letsEncryptConfigured)
        {
            builder.Services
                .AddLettuceEncrypt(options =>
                {
                    options.AcceptTermsOfService = true;
                    options.DomainNames = letsEncryptDomains;
                    options.EmailAddress = letsEncryptEmail!;
                })
                .PersistDataToDirectory(new DirectoryInfo("/var/letsencrypt-certs"), pfxPassword: null);
            return;
        }

        // Sem LettuceEncrypt configurado: largar https://+:443 do bind do Kestrel
        // para evitar "no certificate" exceptions em arranque local/dev.
        var requestedUrls = builder.Configuration["urls"]
            ?? builder.Configuration["ASPNETCORE_URLS"];

        if (string.IsNullOrWhiteSpace(requestedUrls))
        {
            return;
        }

        var httpOnly = string.Join(
            ';',
            requestedUrls
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(u => !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)));

        if (string.IsNullOrEmpty(httpOnly))
        {
            httpOnly = "http://+:80";
        }

        if (!string.Equals(httpOnly, requestedUrls, StringComparison.Ordinal))
        {
            Log.Warning(
                "LettuceEncrypt is not configured (LETSENCRYPT__EMAIL and LETSENCRYPT__DOMAINNAME); "
                + "dropping HTTPS endpoints. Listening on {Urls}.",
                httpOnly);
            builder.WebHost.UseUrls(httpOnly);
        }
    }
}
