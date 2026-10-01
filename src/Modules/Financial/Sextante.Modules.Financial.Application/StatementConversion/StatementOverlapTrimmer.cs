using Sextante.Modules.Financial.Application.Features.Accounts;
using Sextante.Modules.Financial.Domain.Common;

namespace Sextante.Modules.Financial.Application.StatementConversion;

/// <summary>
/// Corta de um extrato as linhas que a conta já tem (D7). Nunca só por data nem
/// só pelo dedup heurístico: dois movimentos idênticos no mesmo dia não se
/// distinguem por data/valor/descrição, mas distinguem-se pelo saldo.
/// </summary>
public sealed class StatementOverlapTrimmer(
    IAccountBalanceQuery balances,
    IAccountLastMovementQuery lastMovement)
{
    public async Task<StatementConversionResult> TrimAsync(
        StatementConversionResult result, Guid accountId, CancellationToken ct)
    {
        var last = await lastMovement.GetLastMovementDateAsync(accountId, ct);
        if (last is not { } lastDate)
            return result;

        return result.HasRowBalances
            ? await TrimByBalanceAnchorAsync(result, accountId, lastDate, ct)
            : await TrimCardAsync(result, accountId, lastDate, ct);
    }

    /// <summary>
    /// Contas com saldo por linha (XLSX, Coverflex): ancora no saldo da conta no fim
    /// do último dia (<c>B</c>) e corta até à última linha com <c>Saldo == B</c> e <c>Data ≤ L</c>.
    /// </summary>
    private async Task<StatementConversionResult> TrimByBalanceAnchorAsync(
        StatementConversionResult result, Guid accountId, DateOnly lastDate, CancellationToken ct)
    {
        if (!result.Rows.Any(r => r.Date <= lastDate))
            return result; // o extrato começa depois do último movimento: nada a cortar

        var balance = await balances.GetBalanceAsync(accountId, lastDate, ct);
        if (balance is null)
            return result;

        var anchor = -1;
        for (var i = 0; i < result.Rows.Count; i++)
        {
            if (result.Rows[i].Date <= lastDate && result.Rows[i].Balance == balance.Amount)
                anchor = i;
        }

        if (anchor < 0)
        {
            throw new StatementValidationException(
                $"O extrato e a conta divergem: o saldo da conta a {StatementText.Date(lastDate)} é {StatementText.Amount(balance.Amount)}, " +
                "mas nenhuma linha do extrato até essa data tem esse saldo. Confirme que escolheu a conta certa e que não há movimentos em falta ou a mais.");
        }

        var kept = result.Rows.Skip(anchor + 1).ToList();
        var check = new StatementCheck(
            $"Saldo da conta a {StatementText.Date(lastDate)} coincide com o extrato",
            true,
            StatementText.Amount(balance.Amount),
            StatementText.Amount(result.Rows[anchor].Balance!.Value));

        return result with
        {
            Rows = kept,
            RowsTrimmed = result.RowsTrimmed + anchor + 1,
            BalanceBefore = balance.Amount,
            Checks = [.. result.Checks, check],
        };
    }

    /// <summary>
    /// Cartão (sem saldo por linha). Extrato inteiro depois do último movimento: nada a
    /// cortar, mas a dívida anterior tem de bater com o saldo da conta no dia anterior ao
    /// período. Extrato que já toca o que existe: cortam-se as linhas com <c>Data ≤ L</c>; sem
    /// verificação da dívida porque as linhas do 1.º dia do ciclo podem ter data anterior ao
    /// período e já estar importadas (o balanço do dia anterior não as exclui).
    /// </summary>
    private async Task<StatementConversionResult> TrimCardAsync(
        StatementConversionResult result, Guid accountId, DateOnly lastDate, CancellationToken ct)
    {
        if (result.PeriodStart is not { } periodStart)
            return result;

        if (periodStart > lastDate)
        {
            var dayBefore = periodStart.AddDays(-1);
            var balance = await balances.GetBalanceAsync(accountId, dayBefore, ct);
            if (balance is not null && result.BalanceBefore is { } debtBefore && balance.Amount != debtBefore)
            {
                throw new StatementValidationException(
                    $"A dívida anterior do extrato ({StatementText.Amount(-debtBefore)}) não bate com o saldo do cartão a " +
                    $"{StatementText.Date(dayBefore)} ({StatementText.Amount(-balance.Amount)} em dívida). " +
                    "Falta importar o extrato anterior, ou a conta escolhida não é a deste cartão.");
            }

            return balance is null || result.BalanceBefore is null
                ? result
                : result with
                {
                    Checks =
                    [
                        .. result.Checks,
                        new StatementCheck(
                            $"Dívida anterior = saldo do cartão a {StatementText.Date(dayBefore)}",
                            true,
                            StatementText.Amount(-result.BalanceBefore.Value),
                            StatementText.Amount(-balance.Amount)),
                    ],
                };
        }

        var kept = result.Rows.Where(r => r.Date > lastDate).ToList();
        return result with
        {
            Rows = kept,
            RowsTrimmed = result.RowsTrimmed + (result.Rows.Count - kept.Count),
        };
    }
}
