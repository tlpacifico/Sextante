using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Domain.Entities;

public sealed class Tenant : IAuditable
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }
}
