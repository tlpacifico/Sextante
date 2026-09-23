using System.Globalization;
using Sextante.Modules.Financial.Domain.Accounts;
using Sextante.Modules.Financial.Domain.ImportProfiles;
using Sextante.Modules.Financial.Domain.Transactions;

namespace Sextante.Modules.Financial.Application.Features.CsvImport;

/// <summary>
/// Uma linha do CSV já interpretada. <see cref="Direction"/> vem do sinal
/// (depois do indicador D/C), nunca da categoria (Phase 6.5 R1).
/// </summary>
public sealed record ParsedImportRow(
    DateOnly Date,
    decimal SignedAmount,
    TransactionDirection Direction,
    decimal AbsAmount,
    string? Currency,
    string? Description,
    string? AccountName);

/// <summary>
/// Phase 6.5 grupo 7 — parse partilhado pelo preview (upload/UpdatePreview)
/// e pelo confirm, com as mesmas definições gravadas no lote (Q3): o que o
/// preview mostra é o que se importa.
/// </summary>
public static class ImportRowParser
{
    /// <summary>
    /// <c>null</c> + <paramref name="error"/> quando falta a data, a data ou o
    /// valor não se leem, ou o valor é zero.
    /// </summary>
    public static ParsedImportRow? Parse(
        IReadOnlyList<string> values,
        CsvColumnResolver resolver,
        ImportParseSettings settings,
        out string? error)
    {
        var list = values as List<string> ?? values.ToList();
        error = null;

        if (!resolver.TryGetDate(list, out var dateStr))
        {
            error = "Data em falta.";
            return null;
        }

        if (!TryParseDate(dateStr, settings.DateFormat, out var date))
        {
            error = $"Data inválida: '{dateStr}'";
            return null;
        }

        if (!resolver.TryGetAmount(list, settings.DecimalSeparator, out var amount))
        {
            error = "Valor inválido ou em falta.";
            return null;
        }

        var indicator = resolver.TryGetCreditDebitIndicator(list, out var ind)
            ? ind.Trim().ToUpperInvariant()
            : null;
        if (indicator is "DEBIT" or "D")
        {
            amount = -Math.Abs(amount);
        }

        if (amount == 0)
        {
            error = "Valor igual a zero.";
            return null;
        }

        return new ParsedImportRow(
            date,
            amount,
            amount > 0 ? TransactionDirection.Inflow : TransactionDirection.Outflow,
            Math.Abs(amount),
            resolver.TryGetCurrency(list, out var currency) ? currency : null,
            resolver.TryGetDescription(list, out var description) ? description : null,
            resolver.TryGetAccount(list, out var accountName) ? accountName.Trim() : null);
    }

    /// <summary>
    /// Mapeamentos gravados no lote ganham ao perfil; sem eles, perfil ou
    /// auto-deteção (comportamento de lotes anteriores ao grupo 7).
    /// </summary>
    public static CsvColumnResolver ResolverFor(
        IReadOnlyList<string> headers, ImportParseSettings? settings, ImportProfile? profile)
        => settings?.ColumnMappings is { } mappings
            ? new CsvColumnResolver(headers, mappings)
            : new CsvColumnResolver(headers, profile);

    /// <summary>
    /// Conta da linha: coluna Conta (por nome) quando mapeada e preenchida;
    /// senão a conta escolhida no wizard (Q2).
    /// </summary>
    public static Account? ResolveAccount(
        ParsedImportRow row, Account batchAccount, IReadOnlyList<Account> accounts, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(row.AccountName))
        {
            return batchAccount;
        }

        var account = accounts.FirstOrDefault(a =>
            string.Equals(a.Name.Trim(), row.AccountName, StringComparison.OrdinalIgnoreCase));
        if (account is null)
        {
            error = $"Conta '{row.AccountName}' não encontrada.";
        }

        return account;
    }

    public static bool TryParseDate(string? dateStr, string? dateFormat, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        var format = dateFormat ?? "dd-MM-yyyy";

        if (DateOnly.TryParseExact(dateStr.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (DateOnly.TryParse(dateStr.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (DateOnly.TryParse(dateStr.Trim(), new CultureInfo("pt-PT"), DateTimeStyles.None, out date))
            return true;

        return false;
    }
}
