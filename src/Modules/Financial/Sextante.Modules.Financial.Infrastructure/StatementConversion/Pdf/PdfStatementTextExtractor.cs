using Sextante.Modules.Financial.Domain.Common;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Sextante.Modules.Financial.Infrastructure.StatementConversion.Pdf;

/// <summary>Palavra com a posição horizontal (pontos PDF) para decidir colunas.</summary>
public sealed record PdfWord(string Text, double X0, double X1);

/// <summary>Linha visual: palavras da mesma linha, ordenadas da esquerda para a direita.</summary>
public sealed record PdfLine(IReadOnlyList<PdfWord> Words)
{
    public string Text => string.Join(' ', Words.Select(w => w.Text));
}

/// <summary>Linhas de uma página, de cima para baixo.</summary>
public sealed record PdfPageContent(int Number, IReadOnlyList<PdfLine> Lines);

/// <summary>
/// PDF → palavras com coordenadas, agrupadas em linhas (D4). Separado dos
/// parsers de layout, que só veem o <see cref="PdfPageContent"/>.
/// </summary>
public static class PdfStatementTextExtractor
{
    public const int MaxPages = 30;
    private const double LineTolerance = 2.5;

    /// <summary>Texto rodado na margem esquerda (rodapé vertical do banco) sai como letras soltas.</summary>
    private const double LeftMarginX = 30;

    public static IReadOnlyList<PdfPageContent> Extract(Stream stream, CancellationToken ct)
    {
        try
        {
            var options = new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true };
            using var document = PdfDocument.Open(stream, options);
            if (document.NumberOfPages > MaxPages)
                throw new StatementUnreadableException($"o PDF tem mais de {MaxPages} páginas.");

            var pages = new List<PdfPageContent>();
            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                pages.Add(new PdfPageContent(page.Number, GroupLines(page)));
            }

            return pages;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or FinancialDomainException))
        {
            // Mensagem genérica: o texto da exceção pode conter conteúdo do ficheiro.
            throw new StatementUnreadableException("o ficheiro não é um PDF legível (corrompido ou protegido por palavra-passe).");
        }
    }

    private static IReadOnlyList<PdfLine> GroupLines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.BoundingBox.Right > LeftMarginX)
            .Select(w => (Word: new PdfWord(w.Text, w.BoundingBox.Left, w.BoundingBox.Right), Y: w.BoundingBox.Bottom))
            .OrderByDescending(w => w.Y)
            .ToList();

        var lines = new List<(double Y, List<PdfWord> Words)>();
        foreach (var (word, y) in words)
        {
            var line = lines.Count > 0 && Math.Abs(lines[^1].Y - y) <= LineTolerance ? lines[^1] : default;
            if (line.Words is null)
            {
                lines.Add((y, [word]));
            }
            else
            {
                line.Words.Add(word);
            }
        }

        return lines
            .Select(l => new PdfLine(l.Words.OrderBy(w => w.X0).ToList()))
            .ToList();
    }
}
