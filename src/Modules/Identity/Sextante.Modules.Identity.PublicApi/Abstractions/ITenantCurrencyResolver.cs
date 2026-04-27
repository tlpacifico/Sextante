namespace Sextante.Modules.Identity.PublicApi.Abstractions;

/// <summary>
/// Resolve a moeda primária do tenant ativo. Implementação Phase 2 lê
/// <c>shared.Tenants.PrimaryCurrency</c> com default <c>'EUR'</c>.
/// Phase 3 abre a porta a multi-moeda real (ECB rates) — esta abstração
/// fica estável.
/// </summary>
public interface ITenantCurrencyResolver
{
    /// <summary>
    /// Devolve o código ISO 4217 (3 letras maiúsculas) da moeda primária
    /// do tenant atual.
    /// </summary>
    Task<string> GetPrimaryCurrencyAsync(CancellationToken cancellationToken);
}
