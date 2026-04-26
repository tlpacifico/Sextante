using Microsoft.AspNetCore.Identity;

namespace Sextante.Modules.Identity.Domain.Entities;

/// <summary>
/// Role aplicacional. Em Phase 1a só serve de placeholder para o
/// <c>SystemAdmin</c> (tech-stack §4.5), populado manualmente.
/// Bootstrap automático fica fora do scope desta phase.
/// </summary>
public sealed class AppRole : IdentityRole<Guid>;
