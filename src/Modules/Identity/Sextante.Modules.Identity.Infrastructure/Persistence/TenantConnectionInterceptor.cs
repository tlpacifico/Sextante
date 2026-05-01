using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sextante.Modules.Identity.PublicApi.Abstractions;

namespace Sextante.Modules.Identity.Infrastructure.Persistence;

public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    public static readonly Guid AnonymousTenantSentinel =
        new("ffffffff-ffff-ffff-ffff-ffffffffffff");

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

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        await ResetTenantAsync(connection, CancellationToken.None).ConfigureAwait(false);
        return await base.ConnectionClosingAsync(connection, eventData, result).ConfigureAwait(false);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ResetTenant(connection);
        return base.ConnectionClosing(connection, eventData, result);
    }

    private Guid ResolveTenantOrSentinel()
    {
        // Tenta resolver via ITenantContext primeiro — cobre o caso de
        // ITenantContextSetter.SetCurrent (jobs Hangfire sem HttpContext).
        try
        {
            return _tenantContext.TenantId.Value;
        }
        catch (UnauthorizedAccessException)
        {
            // Sem tenant — request anónima ou job sem wrapper.
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

    private static async Task ResetTenantAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT set_config('app.current_tenant_id', 'ffffffff-ffff-ffff-ffff-ffffffffffff', false)";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static void ResetTenant(DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT set_config('app.current_tenant_id', 'ffffffff-ffff-ffff-ffff-ffffffffffff', false)";
            cmd.ExecuteNonQuery();
        }
        catch
        {
        }
    }
}
