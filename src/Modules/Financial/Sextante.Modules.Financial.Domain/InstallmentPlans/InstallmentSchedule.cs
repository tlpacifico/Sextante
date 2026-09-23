namespace Sextante.Modules.Financial.Domain.InstallmentPlans;

/// <summary>Prestação n.º <see cref="Number"/> (1..N) de um plano.</summary>
public sealed record Installment(int Number, DateOnly Date, decimal Amount);

/// <summary>
/// Calendário de um plano de prestações (Phase 6.5 grupo 6): prestação
/// <c>i</c> em <c>1.ª + (i − 1) meses</c>, sempre contado a partir da 1.ª
/// (31/01 → 28/02 → 31/03); valor = total / N arredondado a 2 casas para
/// baixo, com a última a absorver os cêntimos que sobram.
/// </summary>
public static class InstallmentSchedule
{
    public static IReadOnlyList<Installment> Build(decimal totalAmount, int count, DateOnly firstInstallmentDate)
    {
        var regular = Math.Round(totalAmount / count, 2, MidpointRounding.ToZero);
        var last = totalAmount - (regular * (count - 1));

        var installments = new List<Installment>(count);
        for (var number = 1; number <= count; number++)
        {
            installments.Add(new Installment(
                number,
                firstInstallmentDate.AddMonths(number - 1),
                number == count ? last : regular));
        }

        return installments;
    }
}
