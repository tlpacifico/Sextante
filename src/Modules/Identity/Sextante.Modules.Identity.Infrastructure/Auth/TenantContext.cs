using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.Modules.Identity.PublicApi.Auth;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Infrastructure.Auth;

public sealed class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public TenantId TenantId
    {
        get
        {
            var http = _httpContextAccessor.HttpContext
                ?? throw new UnauthorizedAccessException(
                    "TenantContext acedido fora de um pipeline HTTP — endpoint anonymous?");

            if (http.User.Identity?.IsAuthenticated != true)
            {
                throw new UnauthorizedAccessException(
                    "TenantContext acedido em request não autenticada — endpoint deveria estar [AllowAnonymous].");
            }

            var claim = http.User.FindFirst(IdentityClaimTypes.TenantId)?.Value
                ?? throw new UnauthorizedAccessException(
                    $"Claim '{IdentityClaimTypes.TenantId}' ausente no JWT — fail-loud.");

            if (!Guid.TryParse(claim, out var tenantGuid))
            {
                throw new UnauthorizedAccessException(
                    $"Claim '{IdentityClaimTypes.TenantId}' tem formato inválido: {claim}");
            }

            return new TenantId(tenantGuid);
        }
    }
}
