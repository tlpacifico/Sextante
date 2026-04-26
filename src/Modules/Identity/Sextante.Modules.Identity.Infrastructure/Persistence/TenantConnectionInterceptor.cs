using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Aplica <c>SET app.current_tenant_id = '&lt;guid&gt;'</c> em cada checkout
/// de connection do pool, alimentando a policy RLS <c>tenant_isolation</c>.
/// </summary>
/// <remarks>
/// <para>Apenas executa quando há <see cref="HttpContext"/> autenticado;
/// requests anonymous (signup, login pré-auth) ficam sem
/// <c>app.current_tenant_id</c> e a RLS bloqueia qualquer query a tabelas
/// tenant-owned (fail-loud).</para>
/// <para>Migrations correm fora do pipeline HTTP, com o role
/// <c>sextante_migrations</c> que tem <c>BYPASSRLS</c> — não precisam do
/// <c>SET</c>.</para>
/// </remarks>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
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
        if (!ShouldApplyTenant())
        {
            await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tenantId = _tenantContext.TenantId.Value;
        await SetTenantAsync(connection, tenantId, cancellationToken).ConfigureAwait(false);

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        if (!ShouldApplyTenant())
        {
            base.ConnectionOpened(connection, eventData);
            return;
        }

        var tenantId = _tenantContext.TenantId.Value;
        SetTenant(connection, tenantId);

        base.ConnectionOpened(connection, eventData);
    }

    private bool ShouldApplyTenant()
    {
        var http = _httpContextAccessor.HttpContext;
        return http?.User.Identity?.IsAuthenticated == true;
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
