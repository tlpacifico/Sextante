using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Sextante.Modules.Financial.Domain.Common;

namespace Sextante.Modules.Financial.Infrastructure.StatementConversion.Pdf;

public enum CardLayout
{
    /// <summary>Até agosto/2026: datas <c>AAAA/MM/DD</c> numa só linha.</summary>
    A,

    /// <summary>Desde setembro/2026: datas <c>MM/DD</c>, descrição em várias linhas, coluna "Rede".</summary>
    B,
}

/// <summary>Movimento do cartão com o valor em módulo e o sentido dado pela coluna (D4).</summary>
public sealed record CardMovement(DateOnly Date, DateOnly ValueDate, string Description, decimal Amount, bool IsCredit);

/// <summary>"RESUMO DE MOVIMENTOS": dívida anterior, créditos, débitos e dívida atual.</summary>
public sealed record CardSummary(decimal PreviousDebt, decimal Credits, decimal Debits, decimal CurrentDebt);

/// <summary>Bloco "RESUMO DA CONTA" + "RESUMO DE MOVIMENTOS", comum aos 2 layouts.</summary>
public sealed record CardStatementHeader(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Currency,
    string? CardHint,
    CardSummary Summary);

internal static partial class CardText
{
    public static readonly Regex LongDate = LongDateRegex();
    public static readonly Regex ShortDate = ShortDateRegex();
    private static readonly Regex AmountPattern = AmountRegex();

    /// <summary>Maiúsculas sem acentos, para comparar títulos e cabeçalhos.</summary>
    public static string Norm(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().ToUpperInvariant();
    }

    public static bool TryParseLongDate(string text, out DateOnly date)
        => DateOnly.TryParseExact(text, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Valores como <c>1 439.00</c> (milhares com espaço, ponto decimal).</summary>
    public static decimal ParseAmount(string text)
        => decimal.Parse(text.Replace(" ", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture);

    public static IReadOnlyList<decimal> FindAmounts(string text)
        => AmountPattern.Matches(text).Select(m => ParseAmount(m.Value)).ToList();

    [GeneratedRegex(@"^\d{4}/\d{2}/\d{2}$")]
    private static partial Regex LongDateRegex();

    [GeneratedRegex(@"^\d{2}/\d{2}$")]
    private static partial Regex ShortDateRegex();

    [GeneratedRegex(@"\d{1,3}(?: \d{3})*\.\d{2}")]
    private static partial Regex AmountRegex();
}

/// <summary>Cabeçalho comum dos 2 layouts: período, moeda, cartão e resumo de movimentos.</summary>
public static class CardStatementHeaderParser
{
    public static CardStatementHeader Parse(IReadOnlyList<PdfPageContent> pages)
    {
        var lines = pages.SelectMany(p => p.Lines).ToList();

        var periodLine = lines.FirstOrDefault(l => l.Words.Count > 1
            && CardText.Norm(l.Words[0].Text) == "EXTRATO" && CardText.Norm(l.Words[1].Text) == "DE:");
        var dates = periodLine?.Words
            .Select(w => CardText.TryParseLongDate(w.Text, out var d) ? d : (DateOnly?)null)
            .Where(d => d is not null).Select(d => d!.Value).ToList();
        if (dates is not { Count: >= 2 })
            throw new StatementLayoutNotSupportedException("não encontrei o período \"Extrato de:\" no PDF.");

        var currency = WordAfter(periodLine!, "MOEDA:")?.ToUpperInvariant() ?? "EUR";

        var cardLine = lines.FirstOrDefault(l => l.Words.Count > 2
            && CardText.Norm(l.Words[0].Text) == "CONTA" && CardText.Norm(l.Words[1].Text) == "CARTAO:");
        var hint = cardLine?.Words[2].Text;

        return new CardStatementHeader(dates[0], dates[1], currency, hint, ParseSummary(lines));
    }

    private static CardSummary ParseSummary(IReadOnlyList<PdfLine> lines)
    {
        var heading = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (CardText.Norm(lines[i].Text) == "RESUMO DE MOVIMENTOS")
            {
                heading = i;
                break;
            }
        }

        if (heading < 0)
            throw new StatementLayoutNotSupportedException("não encontrei o \"RESUMO DE MOVIMENTOS\" no PDF.");

        for (var i = heading + 1; i < Math.Min(lines.Count, heading + 8); i++)
        {
            var text = lines[i].Text;
            if (!lines[i].Words.All(w => Regex.IsMatch(w.Text, @"^(\d{1,3}|\d+\.\d{2})$"))) continue;

            var amounts = CardText.FindAmounts(text);
            if (amounts.Count == 4)
                return new CardSummary(amounts[0], amounts[1], amounts[2], amounts[3]);
        }

        throw new StatementLayoutNotSupportedException("não consegui ler os 4 valores do \"RESUMO DE MOVIMENTOS\".");
    }

    private static string? WordAfter(PdfLine line, string normalizedLabel)
    {
        for (var i = 0; i < line.Words.Count - 1; i++)
        {
            if (CardText.Norm(line.Words[i].Text) == normalizedLabel)
                return line.Words[i + 1].Text;
        }

        return null;
    }
}

public static class CardLayoutDetector
{
    /// <summary>
    /// A primeira linha de movimento decide (2 datas <c>AAAA/MM/DD</c> → A; 2 datas
    /// <c>MM/DD</c> → B); sem movimentos, decide o cabeçalho ("Movimento" → A,
    /// "Mov." → B). Nada reconhecido → erro: nunca adivinhar.
    /// </summary>
    public static CardLayout Detect(IReadOnlyList<PdfPageContent> pages)
    {
        var inMovements = false;
        foreach (var line in pages.SelectMany(p => p.Lines))
        {
            if (CardMovementsReader.IsMovementsHeading(line))
            {
                inMovements = true;
                continue;
            }

            if (!inMovements) continue;
            if (CardMovementsReader.IsOtherSectionHeading(line))
            {
                inMovements = false;
                continue;
            }

            if (line.Words.Count >= 2)
            {
                if (CardText.LongDate.IsMatch(line.Words[0].Text) && CardText.LongDate.IsMatch(line.Words[1].Text))
                    return CardLayout.A;
                if (CardText.ShortDate.IsMatch(line.Words[0].Text) && CardText.ShortDate.IsMatch(line.Words[1].Text))
                    return CardLayout.B;
            }

            var first = CardText.Norm(line.Words[0].Text);
            if (first == "MOV.") return CardLayout.B;
            if (first == "MOVIMENTO") return CardLayout.A;
        }

        throw new StatementLayoutNotSupportedException(
            "não reconheci o formato da tabela \"DETALHE DOS MOVIMENTOS\" (nem datas AAAA/MM/DD nem MM/DD).");
    }
}

/// <summary>Layout A (até agosto/2026): <c>AAAA/MM/DD AAAA/MM/DD descrição … valor</c>.</summary>
public static class CardLayoutAParser
{
    public static IReadOnlyList<CardMovement> Parse(IReadOnlyList<PdfPageContent> pages)
        => CardMovementsReader.Read(pages, CardLayout.A, periodEnd: null);
}

/// <summary>
/// Layout B (desde setembro/2026): <c>MM/DD MM/DD descrição … Rede valor</c> com a
/// descrição a continuar nas linhas seguintes; o ano deduz-se do período (D6).
/// </summary>
public static class CardLayoutBParser
{
    public static IReadOnlyList<CardMovement> Parse(IReadOnlyList<PdfPageContent> pages, DateOnly periodEnd)
        => CardMovementsReader.Read(pages, CardLayout.B, periodEnd);
}

internal static class CardMovementsReader
{
    /// <summary>Margem à esquerda do cabeçalho "Débito" onde já pode começar um valor com milhares ("1 439.00").</summary>
    private const double AmountLeadWidth = 40;

    private const double ContinuationTolerance = 8;

    private sealed record Columns(double DebitX1, double CreditX1, double AmountStartX, double? DescriptionX0, double? NetworkX0, double? NetworkX1);

    public static bool IsMovementsHeading(PdfLine line)
        => CardText.Norm(line.Text) == "DETALHE DOS MOVIMENTOS";

    /// <summary>Título de outra secção (maiúsculas à esquerda): termina a tabela de movimentos.</summary>
    public static bool IsOtherSectionHeading(PdfLine line)
    {
        if (line.Words[0].X0 > 60) return false;
        var text = CardText.Norm(line.Text);
        return text.StartsWith("DETALHE DE ", StringComparison.Ordinal)
               || text.StartsWith("IMPUTACAO ", StringComparison.Ordinal)
               || text.StartsWith("INFORMACAO ", StringComparison.Ordinal)
               || text.StartsWith("RESUMO ", StringComparison.Ordinal)
               || text == "MENSAGENS";
    }

    public static IReadOnlyList<CardMovement> Read(IReadOnlyList<PdfPageContent> pages, CardLayout layout, DateOnly? periodEnd)
    {
        var movements = new List<CardMovement>();
        var inMovements = false;
        Columns? columns = null;
        PendingRow? pending = null;

        void Flush()
        {
            if (pending is not null)
                movements.Add(pending.ToMovement());
            pending = null;
        }

        foreach (var line in pages.SelectMany(p => p.Lines))
        {
            if (IsMovementsHeading(line))
            {
                Flush();
                inMovements = true;
                continue;
            }

            if (!inMovements) continue;

            if (IsOtherSectionHeading(line))
            {
                Flush();
                inMovements = false;
                continue;
            }

            if (TryReadColumns(line) is { } header)
            {
                Flush();
                columns = header;
                continue;
            }

            if (columns is null) continue;

            var row = TryStartRow(line, layout, periodEnd, columns);
            if (row is not null)
            {
                Flush();
                pending = row;
                continue;
            }

            // Só o layout B continua a descrição noutras linhas (o A traz "20.00 USD"
            // e a rede, que não fazem parte da descrição). O texto tem de começar na
            // coluna da descrição: rodapés e outros blocos começam noutro sítio.
            if (layout == CardLayout.B && pending is not null && columns.DescriptionX0 is { } descX
                && Math.Abs(line.Words[0].X0 - descX) <= ContinuationTolerance)
            {
                pending.Description.Add(line.Text);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return movements;
    }

    private static Columns? TryReadColumns(PdfLine line)
    {
        var debit = line.Words.FirstOrDefault(w => CardText.Norm(w.Text) == "DEBITO");
        var credit = line.Words.FirstOrDefault(w => CardText.Norm(w.Text) == "CREDITO");
        if (debit is null || credit is null) return null;

        var description = line.Words.FirstOrDefault(w => CardText.Norm(w.Text) == "DESCRITIVO");
        var network = line.Words.FirstOrDefault(w => CardText.Norm(w.Text) == "REDE");
        return new Columns(debit.X1, credit.X1, debit.X0 - AmountLeadWidth, description?.X0, network?.X0, network?.X1);
    }

    private static PendingRow? TryStartRow(PdfLine line, CardLayout layout, DateOnly? periodEnd, Columns columns)
    {
        var words = line.Words;
        if (words.Count < 4) return null;

        DateOnly date, valueDate;
        if (layout == CardLayout.A)
        {
            if (!CardText.TryParseLongDate(words[0].Text, out date) || !CardText.TryParseLongDate(words[1].Text, out valueDate))
                return null;
        }
        else
        {
            if (!CardText.ShortDate.IsMatch(words[0].Text) || !CardText.ShortDate.IsMatch(words[1].Text))
                return null;
            if (!TryResolveShortDates(words[0].Text, words[1].Text, periodEnd!.Value, out date, out valueDate))
                return null;
        }

        var amountWords = words.Skip(2).Where(w => w.X0 >= columns.AmountStartX).ToList();
        var amountText = string.Concat(amountWords.Select(w => w.Text));
        if (!System.Text.RegularExpressions.Regex.IsMatch(amountText, @"^\d+\.\d{2}$"))
            throw new StatementValidationException(
                $"Não consegui ler o valor do movimento de {StatementDate(date)} no PDF.");

        var credit = amountWords[^1].X1 > (columns.DebitX1 + columns.CreditX1) / 2;
        var description = words.Skip(2)
            .Where(w => w.X0 < columns.AmountStartX && !(layout == CardLayout.B && IsNetwork(w, columns)))
            .Select(w => w.Text);

        var row = new PendingRow(date, valueDate, CardText.ParseAmount(amountText), credit, stripMarker: layout == CardLayout.B);
        row.Description.Add(string.Join(' ', description));
        return row;
    }

    /// <summary>
    /// "VIS", "MB"… dentro da coluna "Rede", na mesma linha (só no layout B; no A a
    /// descrição passa pela mesma zona — "BOM DIA", "CONT" — e a rede vem noutra linha).
    /// </summary>
    private static bool IsNetwork(PdfWord word, Columns columns)
        => columns.NetworkX0 is { } x0 && columns.NetworkX1 is { } x1
           && word.X0 >= x0 - 8 && word.X1 <= x1 + 12
           && System.Text.RegularExpressions.Regex.IsMatch(word.Text, @"^[A-Z]{2,4}$");

    /// <summary>
    /// Data do movimento: mês ≤ mês do fim do período → ano do fim; senão ano
    /// anterior (extrato de janeiro com movimento de dezembro). Data valor: o
    /// ano do movimento, ou o seguinte se ficasse muito antes (movimento a
    /// 31/12 com valor a 02/01).
    /// </summary>
    private static bool TryResolveShortDates(string mmdd, string valueMmdd, DateOnly periodEnd, out DateOnly date, out DateOnly valueDate)
    {
        date = valueDate = default;
        if (!TryMonthDay(mmdd, out var month, out var day)) return false;
        if (!TryMonthDay(valueMmdd, out var valueMonth, out var valueDay)) return false;

        var year = month <= periodEnd.Month ? periodEnd.Year : periodEnd.Year - 1;
        if (!TryDate(year, month, day, out date)) return false;

        if (!TryDate(date.Year, valueMonth, valueDay, out valueDate)) return false;
        if (valueDate < date.AddDays(-60) && !TryDate(date.Year + 1, valueMonth, valueDay, out valueDate))
            return false;
        return true;
    }

    private static bool TryMonthDay(string text, out int month, out int day)
    {
        month = day = 0;
        var parts = text.Split('/');
        return parts.Length == 2 && int.TryParse(parts[0], out month) && int.TryParse(parts[1], out day);
    }

    private static bool TryDate(int year, int month, int day, out DateOnly date)
    {
        date = default;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        date = new DateOnly(year, month, day);
        return true;
    }

    private static string StatementDate(DateOnly date)
        => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="stripMarker"/>: o layout B prefixa alguns textos com "&gt;"; o A mantém-no
    /// (as regras de categorização existentes já contam com o texto do banco tal como vem).
    /// </summary>
    private sealed class PendingRow(DateOnly date, DateOnly valueDate, decimal amount, bool isCredit, bool stripMarker)
    {
        public List<string> Description { get; } = [];

        public CardMovement ToMovement()
            => new(date, valueDate, Normalize(string.Join(' ', Description)), amount, isCredit);

        private string Normalize(string text)
        {
            var cleaned = text.Replace(';', ',').Replace('"', '\'');
            return string.Join(' ', (stripMarker ? cleaned.TrimStart('>') : cleaned)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
