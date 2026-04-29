namespace Sextante.SharedKernel;

/// <summary>
/// Lookup das moedas ativas (<c>shared.currencies</c> com
/// <see cref="Currency.IsActive"/>=true). Vive em SharedKernel para que
/// handlers Application (Financial e Identity) possam validar inputs
/// contra a allowlist sem violar §3.1 das regras de dependência.
/// </summary>
public interface ICurrencyDirectory
{
    /// <summary>
    /// Devolve os códigos ISO 4217 das moedas ativas, em ordem
    /// alfabética. Cache scoped à request é responsabilidade da
    /// implementação.
    /// </summary>
    Task<IReadOnlyList<string>> GetActiveCodesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Confirma que <paramref name="code"/> existe em
    /// <c>shared.currencies</c> com <see cref="Currency.IsActive"/>=true.
    /// </summary>
    Task<bool> IsActiveAsync(string code, CancellationToken cancellationToken);
}
