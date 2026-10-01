using System.Globalization;
using Sextante.Modules.Financial.Infrastructure.StatementConversion.Pdf;
using PdfPoint = UglyToad.PdfPig.Core.PdfPoint;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Sextante.Modules.Financial.Application.Tests.StatementConverters;

/// <summary>Movimento do cartão a desenhar. <see cref="Continuation"/> só se usa no layout B.</summary>
public sealed record FixtureMovement(
    string Date,
    string ValueDate,
    string Description,
    decimal Amount,
    bool IsCredit = false,
    string Network = "VIS",
    string[]? Continuation = null);

public sealed record FixtureSummary(decimal PreviousDebt, decimal Credits, decimal Debits, decimal CurrentDebt);

/// <summary>
/// Gera extratos de cartão sintéticos com as posições (x) dos extratos reais
/// do ActivoBank, em PDF real (extrator + parser) ou direto como
/// <see cref="PdfPageContent"/> (só o parser). D15: nada de dados reais.
/// </summary>
public sealed class CardPdfFixtureBuilder
{
    private sealed record Item(double X, string Text);

    private readonly List<(int Page, List<Item> Items)> _lines = [];
    private int _page = 1;

    private CardPdfFixtureBuilder() { }

    public static CardPdfFixtureBuilder LayoutA(
        IReadOnlyList<FixtureMovement> movements, FixtureSummary summary,
        string periodStart = "2026/08/01", string periodEnd = "2026/08/31")
    {
        var b = new CardPdfFixtureBuilder();
        b.Header(summary, periodStart, periodEnd);
        b.Line(new Item(57, "DETALHE DE TRANSACOES COM PAGAMENTO FRACIONADO"));
        b.Line(new Item(57, "2026/08/30"), new Item(110, "COMPRA 0000 LOJA PRESTACOES EXEMPLO"));
        b.NewPage();
        b.Line(new Item(57, "DETALHE DOS MOVIMENTOS"));
        b.Line(new Item(57, "Data"), new Item(106, "Data"));
        b.Line(new Item(57, "Movimento"), new Item(106, "Valor"), new Item(160, "Descritivo"),
            new Item(311, "Rede"), new Item(457, "Débito"), new Item(535, "Crédito"));
        foreach (var m in movements)
        {
            b.Line(new Item(57, m.Date), new Item(107, m.ValueDate), new Item(163, m.Description), Amount(m, 487, 567));
            b.Line(new Item(319, m.Network));
        }

        b.Footer();
        return b;
    }

    public static CardPdfFixtureBuilder LayoutB(
        IReadOnlyList<FixtureMovement> movements, FixtureSummary summary,
        string periodStart = "2026/09/01", string periodEnd = "2026/09/30")
    {
        var b = new CardPdfFixtureBuilder();
        b.Header(summary, periodStart, periodEnd);
        b.NewPage();
        b.Line(new Item(57, "DETALHE DOS MOVIMENTOS"));
        b.Line(new Item(57, "Data"), new Item(91, "Data"), new Item(128, "Descritivo"),
            new Item(326, "Rede"), new Item(456, "Débito"), new Item(528, "Crédito"));
        b.Line(new Item(57, "Mov."), new Item(91, "Valor"));
        foreach (var m in movements)
        {
            b.Line(new Item(57, m.Date), new Item(91, m.ValueDate), new Item(129, m.Description),
                new Item(337, m.Network), Amount(m, 490, 567));
            foreach (var extra in m.Continuation ?? [])
                b.Line(new Item(129, extra));
        }

        b.Footer();
        return b;
    }

    /// <summary>Layout sem tabela de movimentos reconhecível (nem datas nem cabeçalho).</summary>
    public static CardPdfFixtureBuilder UnknownLayout(FixtureSummary summary)
    {
        var b = new CardPdfFixtureBuilder();
        b.Header(summary, "2026/08/01", "2026/08/31");
        b.NewPage();
        b.Line(new Item(57, "DETALHE DOS MOVIMENTOS"));
        b.Line(new Item(57, "Fecha"), new Item(160, "Concepto"), new Item(457, "Importe"));
        b.Line(new Item(57, "04-08-2026"), new Item(160, "COMPRA EXEMPLO"), new Item(480, "10,00"));
        return b;
    }

    /// <summary>Extrato com os blocos de resumo mas sem "RESUMO DE MOVIMENTOS".</summary>
    public static CardPdfFixtureBuilder WithoutSummary()
    {
        var b = new CardPdfFixtureBuilder();
        b.Line(new Item(57, "RESUMO DA CONTA"));
        b.Line(new Item(57, "Extrato"), new Item(100, "de:"), new Item(204, "2026/08/01"), new Item(252, "a"), new Item(259, "2026/08/31"));
        b.Line(new Item(57, "DETALHE DOS MOVIMENTOS"));
        return b;
    }

    public IReadOnlyList<PdfPageContent> ToPages()
    {
        return _lines.GroupBy(l => l.Page).OrderBy(g => g.Key)
            .Select(g => new PdfPageContent(g.Key, g.Select(l => new PdfLine(
                l.Items.SelectMany(ToWords).OrderBy(w => w.X0).ToList())).ToList()))
            .ToList();
    }

    public byte[] ToPdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var pageLines in _lines.GroupBy(l => l.Page).OrderBy(g => g.Key))
        {
            var page = builder.AddPage(595, 842);
            var y = 800.0;
            foreach (var line in pageLines)
            {
                foreach (var item in line.Items)
                    page.AddText(Ascii(item.Text), FontSize, new PdfPoint(item.X, y), font);
                y -= 14;
            }
        }

        return builder.Build();
    }

    private const double FontSize = 8;

    /// <summary>
    /// A Helvetica standard do PdfPig não tem todos os acentos ("í"); os PDFs sintéticos vão
    /// sem acentos. O parser compara cabeçalhos sem acentos, por isso o resultado é o mesmo.
    /// </summary>
    private static string Ascii(string text)
        => string.Concat(text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

    private void Header(FixtureSummary summary, string periodStart, string periodEnd)
    {
        Line(new Item(57, "EXTRATO VISA CLASSIC ACTIVOBANK"), new Item(473, "N."), new Item(489, "2026/008"));
        Line(new Item(57, "RESUMO DA CONTA"));
        Line(new Item(57, "Extrato"), new Item(95, "de:"), new Item(227, periodStart), new Item(285, "a"),
            new Item(294, periodEnd), new Item(368, "Moeda:"), new Item(488, "EUR"));
        Line(new Item(57, "Conta"), new Item(91, "Cartão:"), new Item(227, "000000-0000000-00-0"));
        Line(new Item(57, "RESUMO DE MOVIMENTOS"));
        Line(new Item(57, "Saldo em Dívida à Data do"), new Item(264, "Créditos"), new Item(382, "Débitos"), new Item(443, "Saldo em Dívida à Data do"));
        Line(new Item(57, "Extrato Anterior"), new Item(479, "Extrato Atual"));
        Line(
            new Item(60, Money(summary.PreviousDebt)), new Item(266, Money(summary.Credits)),
            new Item(380, Money(summary.Debits)), new Item(521, Money(summary.CurrentDebt)));
    }

    private void Footer()
    {
        Line(new Item(515, "Pág."), new Item(536, "2/2"));
        Line(new Item(334, "+351 210 030 700 (Chamada para a rede fixa nacional)"));
    }

    private static Item Amount(FixtureMovement movement, double debitRight, double creditRight)
    {
        var text = Money(movement.Amount);
        var right = movement.IsCredit ? creditRight : debitRight;
        return new Item(right - Width(text), text);
    }

    /// <summary>"1 439.00": milhares com espaço, ponto decimal, como o banco.</summary>
    private static string Money(decimal value)
    {
        var text = value.ToString("#,##0.00", CultureInfo.InvariantCulture);
        return text.Replace(',', ' ');
    }

    private void Line(params Item[] items) => _lines.Add((_page, [.. items]));

    private void NewPage() => _page++;

    /// <summary>Largura aproximada em Helvetica 8 pt (dígitos 5,56; ponto e espaço 2,78; resto 5,8 — por 10 pt).</summary>
    private static double Width(string text)
        => text.Sum(c => char.IsDigit(c) ? 5.56 : c is '.' or ' ' ? 2.78 : 5.8) * FontSize / 10.0;

    private static IEnumerable<PdfWord> ToWords(Item item)
    {
        var x = item.X;
        foreach (var token in item.Text.Split(' '))
        {
            var width = Width(token);
            if (token.Length > 0)
                yield return new PdfWord(token, x, x + width);
            x += width + Width(" ");
        }
    }
}
