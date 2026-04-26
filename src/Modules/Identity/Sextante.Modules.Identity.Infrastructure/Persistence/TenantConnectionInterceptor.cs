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
/// escreve o sentinel <see cref="AnonymousTenantSentinel"/>, um UUID
/// estruturalmente impossível (proibido por CHECK constraint nas tabelas
/// tenant-owned) — RLS devolve sempre vazio.</para>
/// <para>Em <c>ConnectionClosingAsync</c> faz <c>RESET</c> do GUC,
/// garantindo que mesmo se o próximo checkout não passar por esta
/// interceptor (raw <c>NpgsqlConnection</c>, código não-EF), a connection
/// não traz tenant herdado do uso anterior.</para>
/// <para>O signup endpoint sobrepõe explicitamente o GUC para o tenant
/// recém-criado dentro do scope da transação (<c>set_config(..., true)</c>)
/// antes de inserir Tenant + Membership.</para>
/// </remarks>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// UUID sentinel usado quando não há tenant. Estruturalmente impossível
    /// como TenantId real: forbidden por <c>CHECK</c> constraint em
    /// <c>shared.Tenants.Id</c> e <c>shared.Memberships.tenant_id</c>
    /// (migration <c>HardenTenantSentinelGuard</c>).
    /// </summary>
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
        var http = _httpContextAccessor.HttpContext;
        if (http is null || http.User.Identity?.IsAuthenticated != true)
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

    private static async Task ResetTenantAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            // Repor para o sentinel em vez de RESET — RESET deixaria o GUC
            // não definido, e current_setting('...', false)::uuid lança no
            // próximo checkout antes do interceptor reescrever.
            cmd.CommandText =
                "SELECT set_config('app.current_tenant_id', 'ffffffff-ffff-ffff-ffff-ffffffffffff', false)";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Connection já em close; reset best-effort. ConnectionOpenedAsync
            // do próximo checkout reescreve com o tenant correto de qualquer
            // forma — defense in depth, não invariant.
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
            // Idem ResetTenantAsync.
        }
    }
}
