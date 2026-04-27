using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.Domain.Entities;

public sealed class Tenant : IAuditable
{
    public required Guid Id { get; init; }
    public required string Name { get; set; }

    /// <summary>
    /// ISO 4217 (3 letras maiúsculas). Phase 2 introduz a coluna com default
    /// <c>'EUR'</c> via migration; multi-moeda real (Phase 3) adiciona UI
    /// para mudar.
    /// </summary>
    public string PrimaryCurrency { get; set; } = "EUR";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int Version { get; set; }
}
