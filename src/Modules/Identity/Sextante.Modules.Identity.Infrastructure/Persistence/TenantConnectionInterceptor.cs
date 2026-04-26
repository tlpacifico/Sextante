using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Aplica <c>SET app.current_tenant_id = '&lt;guid&gt;'</c> em cada checkout
/// de connection do pool, alimentando a policy RLS <c>tenant_isolation</c>.
/// </summary>
/// <remarks>
/// <para>Defesa contra leak entre tenants via pool reuse: a interceptor
/// corre <strong>sempre</strong> em <c>ConnectionOpenedAsync</c> e
/// reescreve o GUC. Para requests autenticadas, escreve o tenant atual;
/// para requests anonymous (signup, login pré-auth, health checks),
/// escreve o "sentinel UUID" zero, que nenhuma row real iguala — RLS
/// devolve sempre vazio.</para>
/// <para>O signup endpoint sobrepõe explicitamente o GUC para o tenant
/// recém-criado dentro do scope da transação (<c>set_config(..., true)</c>)
/// antes de inserir Tenant + Membership.</para>
/// </remarks>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// UUID sentinel usado quando não há tenant. Garantido por construção
    /// que nenhum <see cref="Sextante.SharedKernel.GuidV7.NewId"/> bate com
    /// este valor (Guid v7 inclui timestamp non-zero).
    /// </summary>
    public static readonly Guid AnonymousTenantSentinel = Guid.Empty;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;

    public TenantConnectionInterceptor(
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        var tenantId = ResolveTenantOrSentinel();
        await SetTenantAsync(connection, tenantId, cancellationToken).ConfigureAwait(false);

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        var tenantId = ResolveTenantOrSentinel();
        SetTenant(connection, tenantId);

        base.ConnectionOpened(connection, eventData);
    }

    private Guid ResolveTenantOrSentinel()
    {
        var http = _httpContextAccessor.HttpContext;
        if (http?.User.Identity?.IsAuthenticated != true)
        {
            return AnonymousTenantSentinel;
        }

        try
        {
            return _tenantContext.TenantId.Value;
        }
        catch (UnauthorizedAccessException)
        {
            // Request autenticada sem claim tenant_id — fail-safe para o
            // sentinel; o pipeline da request vai falhar mais adiante quando
            // o handler tentar resolver ITenantContext.
            return AnonymousTenantSentinel;
        }
    }

    private static async Task SetTenantAsync(DbConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenant, false)";
        var p = cmd.CreateParameter();
        p.ParameterName = "tenant";
        p.Value = tenantId.ToString();
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void SetTenant(DbConnection connection, Guid tenantId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenant, false)";
        var p = cmd.CreateParameter();
        p.ParameterName = "tenant";
        p.Value = tenantId.ToString();
        cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();
    }
}
