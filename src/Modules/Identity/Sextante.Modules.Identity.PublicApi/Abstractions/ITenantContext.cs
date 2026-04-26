using Sextante.SharedKernel;

namespace Sextante.Modules.Identity.PublicApi.Abstractions;

/// <summary>
/// Resolve o tenant ativo a partir dos claims da request HTTP corrente.
/// </summary>
/// <remarks>
/// <para>Fail-loud por design: lança <see cref="UnauthorizedAccessException"/>
/// quando uma request autenticada não traz o claim <c>tenant_id</c>
/// (tech-stack §3.5, AGENTS.md §3.1). Endpoints <c>[AllowAnonymous]</c>
/// não devem resolver este serviço.</para>
/// <para>Outros módulos consomem <see cref="ITenantContext"/> via DI
/// e não tocam diretamente em <c>HttpContext</c> ou <c>Identity</c>.</para>
/// </remarks>
public interface ITenantContext
{
    TenantId TenantId { get; }
}
