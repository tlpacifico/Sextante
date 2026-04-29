using Hangfire.Dashboard;

namespace Sextante.Host;

/// <summary>
/// Limita acesso à <c>/api/admin/hangfire</c> a utilizadores autenticados
/// com role <c>SystemAdmin</c>.
/// </summary>
public sealed class HangfireSystemAdminFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true
            && http.User.IsInRole("SystemAdmin");
    }
}
