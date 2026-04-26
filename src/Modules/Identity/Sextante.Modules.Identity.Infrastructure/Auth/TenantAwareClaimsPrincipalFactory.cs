using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Npgsql;
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
/// <remarks>
/// A query de memberships <strong>não pode</strong> usar o
/// <c>IdentityDbContext</c> registado em DI: esse usa a connection
/// <c>sextante_app</c>, e durante a autenticação o pipeline ainda é
/// anonymous (o GUC <c>app.current_tenant_id</c> é o sentinel zero,
/// pelo que RLS filtra todas as rows de Memberships). Em alternativa
/// abrimos uma connection direta com o role de migrations
/// (<c>BYPASSRLS</c>) só para esta lookup.
/// </remarks>
public sealed class TenantAwareClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<AppUser, AppRole>
{
    private readonly string _privilegedConnectionString;

    public TenantAwareClaimsPrincipalFactory(
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        IdentityMigrationConnectionString migrationConnectionString)
        : base(userManager, roleManager, optionsAccessor)
    {
        _privilegedConnectionString = migrationConnectionString.Value;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        var memberships = await LoadMembershipsAsync(user.Id);
        if (memberships.Count == 0)
        {
            return identity;
        }

        var primary = memberships
            .OrderBy(m => RolePriority(m.Role))
            .First();

        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, primary.TenantId.ToString()));
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantRole, primary.Role.ToString()));

        return identity;
    }

    private async Task<List<(Guid TenantId, MembershipRole Role)>> LoadMembershipsAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(_privilegedConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT tenant_id, \"Role\" FROM shared.\"Memberships\" " +
            "WHERE \"UserId\" = @userId AND \"DeletedAt\" IS NULL";
        cmd.Parameters.Add(new NpgsqlParameter("userId", userId));

        var result = new List<(Guid, MembershipRole)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var tenantId = reader.GetGuid(0);
            var role = Enum.Parse<MembershipRole>(reader.GetString(1));
            result.Add((tenantId, role));
        }

        return result;
    }

    private static int RolePriority(MembershipRole role) => role switch
    {
        MembershipRole.Owner => 0,
        MembershipRole.Member => 1,
        MembershipRole.ReadOnly => 2,
        _ => int.MaxValue,
    };
}
