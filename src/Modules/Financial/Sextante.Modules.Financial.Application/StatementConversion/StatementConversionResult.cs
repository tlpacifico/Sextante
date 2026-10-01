namespace Sextante.Modules.Financial.Application.StatementConversion;

/// <summary>
/// Linha do extrato já interpretada. <see cref="SignedAmount"/> tem o sinal do
/// ponto de vista da conta (saída negativa). <see cref="Balance"/> só existe
/// quando o extrato traz saldo por linha.
/// </summary>
public sealed record StatementRow(
    DateOnly Date,
    DateOnly ValueDate,
    string Description,
    decimal SignedAmount,
    decimal? Balance);

public sealed record StatementCheck(string Name, bool Passed, string Expected, string Actual);

/// <summary>
/// <see cref="BalanceBefore"/> / <see cref="BalanceAfter"/> estão no sinal da
/// conta (cartão: dívida = saldo negativo). <see cref="SourceAccountHint"/> é
/// só para a Phase 6.7: nunca se persiste, regista em log nem vai na API (D12).
/// </summary>
public sealed record StatementConversionResult(
    StatementFormat Format,
    IReadOnlyList<StatementRow> Rows,
    decimal? BalanceBefore,
    decimal? BalanceAfter,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    IReadOnlyList<StatementCheck> Checks,
    string Currency = "EUR",
    string? SourceAccountHint = null,
    int PendingIgnored = 0,
    int CancelledIgnored = 0,
    int RowsTrimmed = 0)
{
    public bool HasRowBalances => Rows.Count > 0 && Rows.All(r => r.Balance.HasValue);
}
