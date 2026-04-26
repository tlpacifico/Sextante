using Sextante.Modules.Identity.Domain.Enums;
using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Domain.Entities;

public sealed class Membership : ITenantOwned, IAuditable
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required TenantId TenantId { get; set; }
    public required MembershipRole Role { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }
}
