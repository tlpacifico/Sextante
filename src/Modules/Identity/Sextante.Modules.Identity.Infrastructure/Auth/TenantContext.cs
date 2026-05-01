using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sextante.Modules.Identity.PublicApi.Abstractions;
using Sextante.Modules.Identity.PublicApi.Auth;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Infrastructure.Auth;

public sealed class TenantContext : ITenantContext, ITenantContextSetter
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private TenantId? _override;

    public TenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public TenantId TenantId
    {
        get
        {
            if (_override.HasValue)
            {
                return _override.Value;
            }

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

    public void SetCurrent(Guid tenantId)
    {
        if (_httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true)
        {
            throw new InvalidOperationException(
                "ITenantContextSetter não pode ser usado dentro de uma request HTTP autenticada.");
        }

        _override = new TenantId(tenantId);
    }

    public void Clear()
    {
        _override = null;
    }
}
