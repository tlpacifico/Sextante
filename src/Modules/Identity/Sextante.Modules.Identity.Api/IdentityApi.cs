using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Sextante.Modules.Identity.Api.Endpoints;
using Sextante.Modules.Identity.Domain.Entities;

namespace Sextante.Modules.Identity.Api;

public static class IdentityApi
{
    /// <summary>
    /// Mapeia <c>/api/auth/*</c> com endpoints built-in do Identity
    /// (login, refresh, logout, manage/info, forgotPassword, resetPassword,
    /// confirmEmail, resendConfirmationEmail) + signup customizado.
    /// </summary>
    public static IEndpointRouteBuilder MapIdentityModule(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/api/auth");

        auth.MapIdentityApi<AppUser>();
        auth.MapSignup();

        return routes;
    }
}
