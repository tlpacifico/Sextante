using Microsoft.Extensions.Logging;
using Sextante.Messaging;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Wolverine;

namespace Sextante.Modules.Identity.Application.Middleware;

/// <summary>
/// Middleware Wolverine que configura <see cref="ITenantContextSetter"/>
/// quando o handler é invocado fora de um pipeline HTTP — caso típico de
/// subscribers de <see cref="ITenantOwnedIntegrationEvent"/> processados
/// via outbox numa worker thread sem <c>HttpContext</c>.
///
/// Sem este middleware o <see cref="ITenantContext"/> falha-loud
/// (tech-stack §4.4) e qualquer query EF Core com Global Query Filter
/// rebenta. Cumpre o mesmo papel que <c>TenantAwareJob&lt;T&gt;</c> faz
/// para Hangfire (tech-stack §4.6).
///
/// Notas:
/// - O type-check do <see cref="IMessageContext"/> é feito em runtime
///   (não via parâmetro tipado) porque Wolverine source-gen trata
///   parâmetros tipados como DI services a resolver.
/// - O retorno é <see cref="TenantContextScope"/> (não
///   <c>IDisposable?</c>) para que o code generator JasperFx atribua
///   um nome de variável único e não colida com outros middlewares
///   que retornem <c>IDisposable?</c> (e.g. <c>TenantLoggingMiddleware</c>).
/// </summary>
public sealed class TenantSettingMiddleware
{
    public static TenantContextScope? Before(
        IMessageContext context,
        ITenantContextSetter tenantSetter,
        ILogger<TenantSettingMiddleware> logger)
    {
        if (context.Envelope?.Message is not ITenantOwnedIntegrationEvent tenantEvent)
        {
            return null;
        }

        if (tenantEvent.TenantId == Guid.Empty)
        {
            logger.LogWarning(
                "Evento {Type} sem TenantId — handler vai falhar-loud no Global Query Filter",
                tenantEvent.GetType().Name);
            return null;
        }

        try
        {
            tenantSetter.SetCurrent(tenantEvent.TenantId);
        }
        catch (InvalidOperationException)
        {
            // Já estamos dentro de uma request HTTP autenticada (caso raro:
            // Wolverine inline dispatch dentro do mesmo scope da request).
            // O caller já configurou o tenant via JWT claims — nada a fazer.
            return null;
        }

        return new TenantContextScope(tenantSetter);
    }
}

public sealed class TenantContextScope : IDisposable
{
    private readonly ITenantContextSetter _tenantSetter;
    private bool _disposed;

    public TenantContextScope(ITenantContextSetter tenantSetter)
    {
        _tenantSetter = tenantSetter;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _tenantSetter.Clear();
        _disposed = true;
    }
}
