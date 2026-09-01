using System.Globalization;
using System.Text;

namespace Sextante.Modules.Financial.Application.Features.Transactions;

/// <summary>
/// Phase 6 — serializa transações para CSV PT-PT (separador <c>;</c>,
/// decimal <c>,</c>, sem separador de milhares), o mesmo formato que o
/// wizard de import já consome. O BOM é responsabilidade de quem escreve
/// os bytes (o endpoint), não desta classe.
/// </summary>
public static class TransactionCsvWriter
{
    public const string Header =
        "Data;Conta;Categoria;Tipo;Descrição;Valor;Moeda;Câmbio;ValorConvertido;MoedaPrincipal;Origem";

    private const char Separator = ';';
    private const string LineBreak = "\r\n";

    /// <summary>
    /// Decimal com vírgula e sem separador de milhares — grouping em CSV
    /// confunde o parser do Excel.
    /// </summary>
    private static readonly NumberFormatInfo PtNumbers = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = string.Empty,
        NumberGroupSizes = [],
    };

    public static string Write(IEnumerable<TransactionExportRow> rows)
    {
        var builder = new StringBuilder();
        builder.Append(Header).Append(LineBreak);

        foreach (var row in rows)
        {
            var rate = row.ExchangeRateToPrimary;
            var converted = row.Amount * (rate ?? 1.0m);

            builder
                .Append(row.OccurredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append(Separator)
                .Append(Escape(row.AccountName))
                .Append(Separator)
                .Append(Escape(row.CategoryName))
                .Append(Separator)
                .Append(Escape(row.Kind))
                .Append(Separator)
                .Append(Escape(row.Description))
                .Append(Separator)
                .Append(Amount(row.Amount))
                .Append(Separator)
                .Append(Escape(row.Currency))
                .Append(Separator)
                // Vazio quando o câmbio não foi gravado (1.0 implícito):
                // inventar "1" mentiria sobre o que está persistido.
                .Append(rate is null ? string.Empty : Rate(rate.Value))
                .Append(Separator)
                .Append(Amount(converted))
                .Append(Separator)
                .Append(Escape(row.PrimaryCurrency))
                .Append(Separator)
                .Append(Escape(row.Origin))
                .Append(LineBreak);
        }

        return builder.ToString();
    }

    private static string Amount(decimal value)
        => value.ToString("0.00##", PtNumbers);

    private static string Rate(decimal value)
        => value.ToString("0.00####", PtNumbers);

    private static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        var needsQuotes = field.Contains(Separator, StringComparison.Ordinal)
            || field.Contains('"', StringComparison.Ordinal)
            || field.Contains('\n', StringComparison.Ordinal)
            || field.Contains('\r', StringComparison.Ordinal);

        return needsQuotes
            ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : field;
    }
}
