using System.Diagnostics;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using Microsoft.Extensions.Logging;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Domain.Common;

namespace Sextante.Modules.Financial.Infrastructure.StatementConversion;

/// <summary>
/// Histórico da conta à ordem ActivoBank (XLSX). Valida o saldo encadeado
/// linha a linha (<c>saldo anterior + valor == saldo</c>, tolerância zero).
/// </summary>
public sealed class ActivoBankAccountXlsxConverter(ILogger<ActivoBankAccountXlsxConverter> logger) : IStatementConverter
{
    public const int MaxRows = 20_000;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    static ActivoBankAccountXlsxConverter()
    {
        // O ExcelDataReader pede a code page 1252 ao construir a configuração.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public StatementFormat Format => StatementFormat.ActivoBankAccountXlsx;

    public bool CanHandle(string fileName) => fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    public StatementConversionResult Convert(Stream stream, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

        try
        {
            var sheet = ReadFirstSheet(stream, timeoutCts.Token);
            var result = Build(sheet);
            logger.LogInformation(
                "Extrato {Format} convertido: {Rows} linhas, {Checks} verificações em {ElapsedMs} ms",
                Format, result.Rows.Count, result.Checks.Count, watch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new StatementTimeoutException();
        }
        catch (FinancialDomainException ex)
        {
            logger.LogWarning("Extrato {Format} recusado: {Reason}", Format, ex.GetType().Name);
            throw;
        }
    }

    private static List<object?[]> ReadFirstSheet(Stream stream, CancellationToken ct)
    {
        try
        {
            using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (rows.Count > MaxRows + 50)
                    throw new StatementUnreadableException($"o ficheiro tem mais de {MaxRows} linhas.");

                var values = new object?[reader.FieldCount];
                for (var i = 0; i < values.Length; i++)
                    values[i] = reader.GetValue(i);
                rows.Add(values);
            }

            return rows;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or FinancialDomainException))
        {
            throw new StatementUnreadableException("o ficheiro não é um XLSX válido.");
        }
    }

    private static StatementConversionResult Build(List<object?[]> sheet)
    {
        var headerIndex = sheet.FindIndex(r =>
            r.Length > 0 && r[0] is string s && s.Trim().StartsWith("Data Lanc", StringComparison.OrdinalIgnoreCase));
        if (headerIndex < 0)
            throw new StatementLayoutNotSupportedException("não encontrei o cabeçalho \"Data Lanc.\" na folha.");

        var hint = sheet.Count > 0 && sheet[0].Length > 0 ? sheet[0][0] as string : null;
        var currency = (HeaderValue(sheet, headerIndex, "Moeda:") as string)?.Trim().ToUpperInvariant() ?? "EUR";
        var periodStart = HeaderDate(sheet, headerIndex, "Data de:");
        var periodEnd = HeaderDate(sheet, headerIndex, "Data at");

        var rows = new List<StatementRow>();
        for (var i = headerIndex + 1; i < sheet.Count; i++)
        {
            var cells = sheet[i];
            if (cells.All(IsBlank)) continue;

            var line = i + 1;
            if (cells.Length < 5 || cells[0] is not DateTime date)
                throw new StatementValidationException($"Linha {line}: data de lançamento em falta ou inválida.");
            if (cells[1] is not DateTime valueDate)
                throw new StatementValidationException($"Linha {line}: data valor em falta ou inválida.");
            var amount = ReadAmount(cells[3], line, "valor");
            var balance = ReadAmount(cells[4], line, "saldo");

            rows.Add(new StatementRow(
                DateOnly.FromDateTime(date),
                DateOnly.FromDateTime(valueDate),
                CanonicalCsvAdapter.SanitizeDescription(System.Convert.ToString(cells[2], CultureInfo.InvariantCulture) ?? string.Empty),
                amount,
                balance));
        }

        if (rows.Count == 0)
            throw new StatementValidationException("O extrato não tem movimentos.");
        if (rows.Count > MaxRows)
            throw new StatementUnreadableException($"o ficheiro tem mais de {MaxRows} movimentos.");

        // O banco exporta do mais antigo para o mais recente; se vier ao contrário,
        // deteta-se pelo encadeamento (nunca pela data) e normaliza-se.
        var failure = FirstChainFailure(rows);
        if (failure is not null)
        {
            var reversed = Enumerable.Reverse(rows).ToList();
            if (FirstChainFailure(reversed) is null)
                rows = reversed;
            else
                throw new StatementValidationException(failure);
        }

        var before = rows[0].Balance!.Value - rows[0].SignedAmount;
        var after = rows[^1].Balance!.Value;
        var expectedAfter = before + rows.Sum(r => r.SignedAmount);
        var checks = new List<StatementCheck>
        {
            new("Saldo encadeado linha a linha", true,
                StatementText.Amount(expectedAfter), StatementText.Amount(after)),
        };

        return new StatementConversionResult(
            StatementFormat.ActivoBankAccountXlsx,
            rows,
            before,
            after,
            periodStart ?? rows[0].Date,
            periodEnd ?? rows[^1].Date,
            checks,
            currency,
            hint);
    }

    /// <summary>Mensagem da primeira linha cujo saldo não é o anterior + valor (a 1.ª ancora o saldo anterior).</summary>
    private static string? FirstChainFailure(IReadOnlyList<StatementRow> rows)
    {
        var running = rows[0].Balance!.Value - rows[0].SignedAmount;
        for (var i = 0; i < rows.Count; i++)
        {
            running += rows[i].SignedAmount;
            if (running != rows[i].Balance!.Value)
            {
                return $"O saldo não encadeia no movimento {i + 1} ({StatementText.Date(rows[i].Date)}): " +
                       $"esperado {StatementText.Amount(running)}, encontrado {StatementText.Amount(rows[i].Balance!.Value)}.";
            }
        }

        return null;
    }

    private static bool IsBlank(object? cell) => cell is null || (cell is string s && string.IsNullOrWhiteSpace(s));

    private static decimal ReadAmount(object? cell, int line, string field)
    {
        decimal value;
        switch (cell)
        {
            case double d: value = (decimal)d; break;
            case int n: value = n; break;
            case long l: value = l; break;
            case decimal m: value = m; break;
            case string s when decimal.TryParse(s.Replace(" ", "").Replace(',', '.'),
                                   NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed):
                value = parsed; break;
            default:
                throw new StatementValidationException($"Linha {line}: {field} em falta ou inválido.");
        }

        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(value - rounded) > 0.0001m)
            throw new StatementValidationException($"Linha {line}: {field} com mais de 2 casas decimais.");
        return rounded;
    }

    private static DateOnly? HeaderDate(List<object?[]> sheet, int headerIndex, string label)
        => HeaderValue(sheet, headerIndex, label) is DateTime dt ? DateOnly.FromDateTime(dt) : null;

    private static object? HeaderValue(List<object?[]> sheet, int headerIndex, string label)
    {
        for (var i = 0; i < headerIndex; i++)
        {
            var cells = sheet[i];
            if (cells.Length > 1 && cells[0] is string s && s.Trim().StartsWith(label, StringComparison.OrdinalIgnoreCase))
                return cells[1];
        }

        return null;
    }
}
