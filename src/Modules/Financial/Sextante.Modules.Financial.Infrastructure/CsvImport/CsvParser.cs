using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Sextante.Modules.Financial.Application.CsvImport;
using Sextante.Modules.Financial.Domain.Common;

namespace Sextante.Modules.Financial.Infrastructure.CsvImport;

public sealed class CsvParser : ICsvParser
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public CsvParseResult Parse(Stream csvStream, CsvParseOptions options, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        var token = timeoutCts.Token;

        try
        {
            var delimiter = options.Delimiter ?? AutoDetectDelimiter(csvStream, options);
            csvStream.Position = 0;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = delimiter,
                HasHeaderRecord = options.HasHeaderRow,
                MissingFieldFound = null,
                HeaderValidated = null,
                BadDataFound = null,
                TrimOptions = TrimOptions.Trim,
                Encoding = StrictUtf8,
            };

            var rows = TryParse(csvStream, config, token, out var headers, out var encoding);
            if (rows is null)
            {
                csvStream.Position = 0;
                config.Encoding = Encoding.GetEncoding("ISO-8859-1");
                rows = TryParse(csvStream, config, token, out headers, out encoding);
                if (rows is null)
                    throw new CsvEncodingNotSupportedException();
            }

            var skip = options.SkipRows;
            if (skip > 0)
                rows = rows.Skip(skip).ToList();

            if (rows.Count == 0)
                throw new CsvEmptyException();

            if (headers.Count > 0)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in headers)
                {
                    var trimmed = h.Trim();
                    if (!seen.Add(trimmed))
                        throw new CsvDuplicateColumnsException(trimmed);
                }
            }

            var totalRowCount = rows.Count;
            var truncated = totalRowCount > options.MaxPreviewRows;
            var previewRows = truncated
                ? rows.Take(options.MaxPreviewRows).ToList()
                : rows;

            return new CsvParseResult(
                headers,
                previewRows.Select(r => (IReadOnlyList<string>)r).ToList(),
                totalRowCount,
                truncated);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new CsvParseTimeoutException();
        }
    }

    private static string AutoDetectDelimiter(Stream csvStream, CsvParseOptions options)
    {
        var candidates = new[] { ",", ";", "\t", "|" };
        var buffer = new byte[8192];
        var read = csvStream.Read(buffer, 0, buffer.Length);
        var content = Encoding.UTF8.GetString(buffer, 0, read);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var sample = lines.Take(Math.Min(5, lines.Length)).ToArray();

        string bestDelimiter = ";";
        int bestColumns = 0;
        int bestScore = int.MaxValue;

        foreach (var delim in candidates)
        {
            var colCounts = new List<int>();
            foreach (var line in sample)
            {
                var count = CountColumns(line, delim);
                if (count > 1)
                    colCounts.Add(count);
            }
            if (colCounts.Count == 0) continue;

            var avg = colCounts.Average();
            var variance = (int)colCounts.Average(c => Math.Abs(c - avg));
            var maxCols = colCounts.Max();

            if (maxCols > bestColumns || (maxCols == bestColumns && variance < bestScore))
            {
                bestColumns = maxCols;
                bestScore = variance;
                bestDelimiter = delim;
            }
        }

        return bestDelimiter;
    }

    private static int CountColumns(string line, string delimiter)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
        };
        using var reader = new StringReader(line);
        using var csv = new CsvReader(reader, config);
        if (csv.Read())
        {
            return csv.ColumnCount;
        }
        return 0;
    }

    private static List<List<string>>? TryParse(
        Stream csvStream,
        CsvConfiguration config,
        CancellationToken ct,
        out List<string> headers,
        out Encoding encoding)
    {
        headers = new List<string>();
        encoding = config.Encoding ?? Encoding.UTF8;

        try
        {
            using var reader = new StreamReader(csvStream, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            using var csv = new CsvReader(reader, config);

            if (config.HasHeaderRecord)
            {
                if (!csv.Read())
                    return null;
                csv.ReadHeader();
                headers = csv.HeaderRecord?.ToList() ?? new List<string>();
            }

            var rows = new List<List<string>>();
            while (csv.Read())
            {
                ct.ThrowIfCancellationRequested();
                var row = new List<string>();
                for (int i = 0; i < csv.ColumnCount; i++)
                {
                    row.Add(csv.GetField(i) ?? string.Empty);
                }
                rows.Add(row);
            }
            return rows;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
