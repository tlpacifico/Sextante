namespace Sextante.Modules.Financial.Application.ExchangeRates;

/// <summary>
/// Aritmética pura de cross-rate EUR-base. Isolado da camada de
/// persistência para permitir unit tests determinísticos da
/// matemática (<see cref="ExchangeRateService"/> orquestra os
/// lookups e delega para este helper).
/// </summary>
public static class CrossRate
{
    public const string EurBase = "EUR";

    /// <summary>
    /// Calcula <c>from → to</c> dadas as rates EUR-base. Argumentos:
    /// <list type="bullet">
    /// <item><c>eurFromRate</c>: <c>EUR → from</c> (obrigatório quando
    /// <c>from != EUR</c>).</item>
    /// <item><c>eurToRate</c>: <c>EUR → to</c> (obrigatório quando
    /// <c>to != EUR</c>).</item>
    /// </list>
    /// Não trata o caso <c>from == to</c> — chamador deve verificar e
    /// devolver <c>null</c> (rate=1.0 implied) antes.
    /// </summary>
    public static decimal Compute(
        string from,
        string to,
        decimal? eurFromRate,
        decimal? eurToRate)
    {
        if (string.Equals(from, EurBase, StringComparison.Ordinal))
        {
            if (eurToRate is null)
            {
                throw new ArgumentException(
                    $"eurToRate é obrigatório quando from=={EurBase}.",
                    nameof(eurToRate));
            }

            return eurToRate.Value;
        }

        if (string.Equals(to, EurBase, StringComparison.Ordinal))
        {
            if (eurFromRate is null)
            {
                throw new ArgumentException(
                    $"eurFromRate é obrigatório quando to=={EurBase}.",
                    nameof(eurFromRate));
            }

            return 1m / eurFromRate.Value;
        }

        if (eurFromRate is null || eurToRate is null)
        {
            throw new ArgumentException(
                "eurFromRate e eurToRate são obrigatórios para cross-rate non-EUR.");
        }

        return eurToRate.Value / eurFromRate.Value;
    }
}
