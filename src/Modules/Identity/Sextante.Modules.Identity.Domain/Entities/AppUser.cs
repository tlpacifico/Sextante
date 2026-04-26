using Microsoft.AspNetCore.Identity;

namespace Sextante.Modules.Identity.Domain.Entities;

/// <summary>
/// Utilizador da aplicação. Sem campos custom em Phase 1a — extensões
/// para perfil ficam para Phase 1b ou Phase 6.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>;
