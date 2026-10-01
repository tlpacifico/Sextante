using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Domain.Common;

namespace Sextante.Modules.Financial.Infrastructure.StatementConversion;

/// <summary>
/// Movimentos da Coverflex (JSON da API: <c>movements.list</c>). Montantes em
/// cêntimos, só <c>confirmed</c>, data em Lisboa; valida a aritmética por
/// linha e a identidade global — o encadeamento linha a linha não é fiável
/// nesta fonte (D13).
/// </summary>
public sealed partial class CoverflexJsonConverter(ILogger<CoverflexJsonConverter> logger) : IStatementConverter
{
    public const int MaxMovements = 20_000;

    public StatementFormat Format => StatementFormat.CoverflexJson;

    public bool CanHandle(string fileName) => fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public StatementConversionResult Convert(Stream stream, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            var result = Build(Parse(stream));
            logger.LogInformation(
                "Extrato {Format} convertido: {Rows} linhas, {Pending} pendentes, {Cancelled} cancelados, {Checks} verificações em {ElapsedMs} ms",
                Format, result.Rows.Count, result.PendingIgnored, result.CancelledIgnored, result.Checks.Count, watch.ElapsedMilliseconds);
            return result;
        }
        catch (FinancialDomainException ex)
        {
            logger.LogWarning("Extrato {Format} recusado: {Reason}", Format, ex.GetType().Name);
            throw;
        }
    }

    private sealed record Movement(
        int FileIndex,
        string Status,
        DateTimeOffset ExecutedAt,
        string Description,
        long Cents,
        bool IsDebit,
        string Currency,
        long? BalanceBefore,
        long? BalanceAfter)
    {
        public long Signed => IsDebit ? -Cents : Cents;
    }

    private static List<Movement> Parse(Stream stream)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException)
        {
            throw new StatementUnreadableException("o ficheiro não é um JSON válido.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("movements", out var envelope)
                || envelope.ValueKind != JsonValueKind.Object
                || !envelope.TryGetProperty("list", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                throw new StatementLayoutNotSupportedException("o JSON não tem o formato da Coverflex (movements.list).");
            }

            var count = list.GetArrayLength();
            if (count == 0)
                throw new StatementValidationException("O extrato não tem movimentos.");
            if (count > MaxMovements)
                throw new StatementUnreadableException($"o ficheiro tem mais de {MaxMovements} movimentos.");

            var movements = new List<Movement>(count);
            var index = 0;
            foreach (var item in list.EnumerateArray())
                movements.Add(ReadMovement(item, index++));
            return movements;
        }
    }

    private static Movement ReadMovement(JsonElement item, int index)
    {
        var number = index + 1;
        if (item.ValueKind != JsonValueKind.Object)
            throw new StatementValidationException($"Movimento {number}: formato inesperado.");

        var status = RequiredString(item, "status", number);
        var confirmed = status == "confirmed";

        var amount = RequiredObject(item, "amount", number);
        var cents = RequiredCents(amount, number, "amount.amount");
        var currency = RequiredString(amount, "currency", number, "amount.currency").ToUpperInvariant();
        if (cents < 0)
            throw new StatementValidationException($"Movimento {number}: montante negativo (o sentido vem em is_debit).");

        var isDebit = item.TryGetProperty("is_debit", out var debit) && debit.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? debit.GetBoolean()
            : throw new StatementValidationException($"Movimento {number}: campo is_debit em falta.");

        var executedAtText = RequiredString(item, "executed_at", number);
        if (!DateTimeOffset.TryParse(executedAtText, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var executedAt))
            throw new StatementValidationException($"Movimento {number}: executed_at inválido.");

        var description = OptionalString(item, "description") ?? OptionalString(item, "merchant_name") ?? string.Empty;

        long? before = null, after = null;
        if (confirmed)
        {
            before = RequiredCents(RequiredObject(item, "balance_before", number), number, "balance_before.amount");
            after = RequiredCents(RequiredObject(item, "balance_after", number), number, "balance_after.amount");
        }

        return new Movement(index, status, executedAt, description, cents, isDebit, currency, before, after);
    }

    private static StatementConversionResult Build(List<Movement> all)
    {
        foreach (var m in all.Where(m => m.Status is not ("confirmed" or "pending" or "cancelled")))
            throw new StatementValidationException($"Movimento {m.FileIndex + 1}: estado desconhecido '{m.Status}'.");

        var pending = all.Count(m => m.Status == "pending");
        var cancelled = all.Count(m => m.Status == "cancelled");

        // D18: execução ascendente; empate → ordem inversa de chegada (a API devolve do mais recente).
        var confirmed = all.Where(m => m.Status == "confirmed")
            .OrderBy(m => m.ExecutedAt).ThenByDescending(m => m.FileIndex)
            .ToList();
        if (confirmed.Count == 0)
            throw new StatementValidationException("O extrato não tem movimentos confirmados.");

        var currency = confirmed[0].Currency;
        if (confirmed.Any(m => m.Currency != currency))
            throw new StatementValidationException("O extrato mistura várias moedas; só é possível importar movimentos numa moeda.");

        // (a) aritmética por linha.
        foreach (var m in confirmed)
        {
            var expected = m.BalanceBefore!.Value + m.Signed;
            if (expected != m.BalanceAfter!.Value)
            {
                throw new StatementValidationException(
                    $"Movimento {m.FileIndex + 1} ({LisbonDate(m.ExecutedAt):dd/MM/yyyy}): o saldo não bate com o valor — " +
                    $"esperado {Euros(expected)}, encontrado {Euros(m.BalanceAfter.Value)}.");
            }
        }

        // (b) identidade global: saldo inicial + Σ valores = saldo final. A ordem de execução não
        // é a de contabilização, por isso os extremos acham-se pelos saldos (o único saldo que só
        // aparece como "antes" é o inicial; o único que só aparece como "depois", o final), não
        // pela posição das linhas.
        var (initial, final) = FindEnds(confirmed);
        var sum = confirmed.Sum(m => m.Signed);
        if (initial + sum != final)
        {
            throw new StatementValidationException(
                $"O saldo final não bate com a soma dos movimentos: esperado {Euros(initial + sum)}, " +
                $"encontrado {Euros(final)}.");
        }

        var rows = confirmed
            .Select(m =>
            {
                var date = LisbonDate(m.ExecutedAt);
                return new StatementRow(date, date, NormalizeDescription(m.Description), m.Signed / 100m, m.BalanceAfter!.Value / 100m);
            })
            .ToList();

        var checks = new List<StatementCheck>
        {
            new("Aritmética de cada movimento (saldo antes ± valor = saldo depois)", true, $"{confirmed.Count} de {confirmed.Count}", $"{confirmed.Count} de {confirmed.Count}"),
            new("Saldo inicial + Σ movimentos = saldo final", true, Euros(initial + sum), Euros(final)),
        };

        return new StatementConversionResult(
            StatementFormat.CoverflexJson,
            rows,
            initial / 100m,
            final / 100m,
            rows[0].Date,
            rows[^1].Date,
            checks,
            currency,
            SourceAccountHint: null,
            PendingIgnored: pending,
            CancelledIgnored: cancelled);
    }

    /// <summary>
    /// Saldo inicial e final do conjunto: contam-se as vezes que cada saldo aparece como "antes" e
    /// como "depois"; um caminho completo de saldos tem um único saldo com um "antes" a mais (o
    /// inicial) e um único com um "depois" a mais (o final), ou nenhum (saldo final = inicial).
    /// Qualquer outro padrão significa movimentos em falta ou a mais.
    /// </summary>
    private static (long Initial, long Final) FindEnds(IReadOnlyList<Movement> confirmed)
    {
        var surplus = new Dictionary<long, int>();
        foreach (var m in confirmed)
        {
            surplus[m.BalanceBefore!.Value] = surplus.GetValueOrDefault(m.BalanceBefore.Value) + 1;
            surplus[m.BalanceAfter!.Value] = surplus.GetValueOrDefault(m.BalanceAfter.Value) - 1;
        }

        var starts = surplus.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
        var ends = surplus.Where(kv => kv.Value == -1).Select(kv => kv.Key).ToList();
        var wellFormed = surplus.Values.All(v => v is -1 or 0 or 1) && starts.Count == ends.Count && starts.Count <= 1;
        if (!wellFormed)
        {
            throw new StatementValidationException(
                "Os saldos dos movimentos não formam uma sequência única — faltam ou sobram movimentos no ficheiro.");
        }

        if (starts.Count == 1) return (starts[0], ends[0]);

        // Saldo final = saldo inicial (Σ valores = 0): nenhum extremo se destaca; vale o saldo do último por execução.
        var closed = confirmed[^1].BalanceAfter!.Value;
        return (closed, closed);
    }

    /// <summary>
    /// O topup traz ruído variável (<c>CVFX… COVERFLEX TOPUP ITEMID:&lt;guid&gt;</c>); normaliza-se para
    /// as regras de categorização e a deteção de duplicados serem estáveis.
    /// </summary>
    public static string NormalizeDescription(string description)
    {
        var cleaned = CanonicalCsvAdapter.SanitizeDescription(description);
        return TopupNoise().Replace(cleaned, "COVERFLEX TOPUP").Trim();
    }

    [GeneratedRegex(@"^(?:CVFX\S*\s+)?COVERFLEX TOPUP\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex TopupNoise();

    /// <summary>
    /// Data civil em Lisboa. Regra da UE desde 1996: WEST (UTC+1) entre o último domingo de março
    /// e o último domingo de outubro, ambos às 01:00 UTC. Fixa no código (sem depender de tzdata na imagem).
    /// </summary>
    public static DateOnly LisbonDate(DateTimeOffset utc)
    {
        var u = utc.UtcDateTime;
        var start = LastSundayUtc(u.Year, 3);
        var end = LastSundayUtc(u.Year, 10);
        var local = u >= start && u < end ? u.AddHours(1) : u;
        return DateOnly.FromDateTime(local);
    }

    private static DateTime LastSundayUtc(int year, int month)
    {
        var day = new DateTime(year, month, DateTime.DaysInMonth(year, month), 1, 0, 0, DateTimeKind.Utc);
        return day.AddDays(-(int)day.DayOfWeek);
    }

    private static string Euros(long cents) => StatementText.Amount(cents / 100m);

    private static JsonElement RequiredObject(JsonElement parent, string name, int number)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new StatementValidationException($"Movimento {number}: campo {name} em falta.");

    private static string RequiredString(JsonElement parent, string name, int number, string? label = null)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new StatementValidationException($"Movimento {number}: campo {label ?? name} em falta.");

    private static string? OptionalString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Lê <c>amount</c> (inteiro, em cêntimos) de um objeto <c>{ currency, amount }</c>.</summary>
    private static long RequiredCents(JsonElement money, int number, string label)
    {
        if (!money.TryGetProperty("amount", out var value) || value.ValueKind != JsonValueKind.Number)
            throw new StatementValidationException($"Movimento {number}: campo {label} em falta.");
        if (!value.TryGetDecimal(out var d) || d != decimal.Truncate(d))
            throw new StatementValidationException($"Movimento {number}: {label} não é um número inteiro de cêntimos.");
        return (long)d;
    }
}
