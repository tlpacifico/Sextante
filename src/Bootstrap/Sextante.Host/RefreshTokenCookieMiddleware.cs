using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sextante.Host;

/// <summary>
/// Phase 1b — promove o refresh token devolvido pelo <c>MapIdentityApi</c>
/// (login/refresh) num cookie <c>HttpOnly; Secure; SameSite=Strict;
/// Path=/api/auth/refresh; Max-Age=604800</c>. Em logout limpa o cookie.
/// Em <c>POST /api/auth/refresh</c>, se o body não trouxer
/// <c>refreshToken</c> mas o request tiver o cookie, injecta o valor do
/// cookie no body antes de chegar ao handler do Identity.
/// </summary>
/// <remarks>
/// O backend continua a aceitar o refresh token via body para retro-
/// compatibilidade com integration tests da Phase 1a; o frontend Angular
/// passa a depender exclusivamente do cookie.
///
/// Phase 5.5 — suporte a "Manter-me ligado" (extended session):
/// lê <c>extendedSession</c> do body do login para decidir o <c>Max-Age</c>
/// do cookie (default 7 dias vs 30 dias estendido).
/// </remarks>
internal static class RefreshTokenCookieMiddleware
{
    internal const string CookieName = "refresh_token";
    internal const string CookiePath = "/api/auth/refresh";
    internal static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(7);
    internal static readonly TimeSpan ExtendedMaxAge = TimeSpan.FromDays(30);

    private const string LoginPath = "/api/auth/login";
    private const string RefreshPath = "/api/auth/refresh";
    private const string LogoutPath = "/api/auth/logout";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IApplicationBuilder UseRefreshTokenCookie(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path;

            if (HttpMethods.IsPost(context.Request.Method)
                && path.Equals(RefreshPath, StringComparison.OrdinalIgnoreCase))
            {
                await EnsureRefreshBodyAsync(context);
            }

            if (HttpMethods.IsPost(context.Request.Method)
                && (path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase)
                    || path.Equals(RefreshPath, StringComparison.OrdinalIgnoreCase)))
            {
                var isExtended = false;
                if (path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
                {
                    isExtended = await DetectExtendedSessionAsync(context);
                }

                await CaptureAndForwardAsync(context, next, emitCookieOnSuccess: true, isExtended);
                return;
            }

            if (HttpMethods.IsPost(context.Request.Method)
                && path.Equals(LogoutPath, StringComparison.OrdinalIgnoreCase))
            {
                AppendDeleteCookie(context);
                await next();
                return;
            }

            await next();
        });
    }

    private static async Task<bool> DetectExtendedSessionAsync(HttpContext context)
    {
        try
        {
            context.Request.EnableBuffering();
            var bodyBytes = await ReadAllAsync(context.Request.Body);
            context.Request.Body.Position = 0;

            if (bodyBytes.Length > 0)
            {
                using var doc = JsonDocument.Parse(bodyBytes);
                if (doc.RootElement.TryGetProperty("extendedSession", out var ext)
                    && ext.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Se o body não for JSON válido, usa default.
        }
        return false;
    }

    private static async Task EnsureRefreshBodyAsync(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var cookieValue)
            || string.IsNullOrEmpty(cookieValue))
        {
            return;
        }

        context.Request.EnableBuffering();
        var bodyBytes = await ReadAllAsync(context.Request.Body);
        context.Request.Body.Position = 0;

        var hasRefreshToken = false;
        if (bodyBytes.Length > 0)
        {
            try
            {
                using var doc = JsonDocument.Parse(bodyBytes);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("refreshToken", out var rt)
                    && rt.ValueKind == JsonValueKind.String
                    && !string.IsNullOrEmpty(rt.GetString()))
                {
                    hasRefreshToken = true;
                }
            }
            catch (JsonException)
            {
                // Body inválido — deixa o handler do Identity rejeitar.
                return;
            }
        }

        if (hasRefreshToken)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new RefreshRequestBody(cookieValue),
            JsonOptions);

        var newBody = new MemoryStream(payload);
        context.Request.Body = newBody;
        context.Request.ContentLength = payload.LongLength;
        context.Request.ContentType = "application/json";
    }

    private static async Task CaptureAndForwardAsync(
        HttpContext context,
        Func<Task> next,
        bool emitCookieOnSuccess,
        bool isExtended)
    {
        var originalBody = context.Response.Body;
        var originalBodyFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next();
        }
        catch
        {
            context.Response.Body = originalBody;
            throw;
        }

        var statusOk = context.Response.StatusCode is >= 200 and < 300;
        if (emitCookieOnSuccess && statusOk && buffer.Length > 0)
        {
            buffer.Position = 0;
            try
            {
                using var doc = await JsonDocument.ParseAsync(buffer);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("refreshToken", out var rt)
                    && rt.ValueKind == JsonValueKind.String)
                {
                    var refreshToken = rt.GetString();
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        var maxAge = isExtended ? ExtendedMaxAge : DefaultMaxAge;
                        AppendRefreshCookie(context, refreshToken, maxAge);
                    }
                }
            }
            catch (JsonException ex)
            {
                var logger = context.RequestServices
                    .GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                logger?.CreateLogger("RefreshTokenCookieMiddleware")
                    .LogWarning(ex, "Falha ao parsear resposta de auth para extrair refresh token.");
            }
        }

        buffer.Position = 0;
        context.Response.Body = originalBody;
        await buffer.CopyToAsync(originalBody);
    }

    private static void AppendRefreshCookie(HttpContext context, string refreshToken, TimeSpan maxAge)
    {
        var options = BuildCookieOptions(context);
        options.MaxAge = maxAge;
        context.Response.Cookies.Append(CookieName, refreshToken, options);
    }

    private static void AppendDeleteCookie(HttpContext context)
    {
        var options = BuildCookieOptions(context);
        options.MaxAge = TimeSpan.Zero;
        options.Expires = DateTimeOffset.UnixEpoch;
        context.Response.Cookies.Append(CookieName, string.Empty, options);
    }

    private static CookieOptions BuildCookieOptions(HttpContext context)
    {
        var env = context.RequestServices
            .GetService(typeof(IHostEnvironment)) as IHostEnvironment;

        var secure = true;
        if (env is not null && env.IsDevelopment() && !context.Request.IsHttps)
        {
            secure = false;
        }

        return new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
        };
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private sealed record RefreshRequestBody(
        [property: JsonPropertyName("refreshToken")] string RefreshToken);
}
