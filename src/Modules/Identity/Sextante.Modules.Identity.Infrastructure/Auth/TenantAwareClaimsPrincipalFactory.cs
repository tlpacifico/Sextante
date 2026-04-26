using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sextante.Modules.Identity.Domain.Entities;
using Sextante.Modules.Identity.Domain.Enums;
using Sextante.Modules.Identity.Infrastructure.Persistence;
using Sextante.Modules.Identity.PublicApi.Auth;

namespace Sextante.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// Estende a factory default do Identity injetando os claims
/// <c>tenant_id</c> e <c>tenant_role</c>. Em Phase 1a o utilizador
/// tem apenas uma <see cref="Membership"/> (Owner); a ordenação por
/// prioridade fica preparada para Phase 18 (multi-membership UI).
/// </summary>
public sealed class TenantAwareClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<AppUser, AppRole>
{
    private readonly IdentityDbContext _dbContext;

    public TenantAwareClaimsPrincipalFactory(
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        IdentityDbContext dbContext)
        : base(userManager, roleManager, optionsAccessor)
    {
        _dbContext = dbContext;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        // IgnoreQueryFilters porque o ClaimsPrincipal é construído
        // pré-autenticação completa e o ITenantContext ainda não está pronto.
        var memberships = await _dbContext.Memberships
            .IgnoreQueryFilters()
            .Where(m => m.UserId == user.Id && m.DeletedAt == null)
            .ToListAsync();

        if (memberships.Count == 0)
        {
            return identity;
        }

        var primary = memberships
            .OrderBy(m => RolePriority(m.Role))
            .First();

        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, primary.TenantId.Value.ToString()));
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantRole, primary.Role.ToString()));

        return identity;
    }

    private static int RolePriority(MembershipRole role) => role switch
    {
        MembershipRole.Owner => 0,
        MembershipRole.Member => 1,
        MembershipRole.ReadOnly => 2,
        _ => int.MaxValue,
    };
}
