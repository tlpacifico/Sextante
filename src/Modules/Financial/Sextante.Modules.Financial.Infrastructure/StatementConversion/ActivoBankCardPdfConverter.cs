using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sextante.Modules.Financial.Application.StatementConversion;
using Sextante.Modules.Financial.Domain.Common;
using Sextante.Modules.Financial.Infrastructure.StatementConversion.Pdf;

namespace Sextante.Modules.Financial.Infrastructure.StatementConversion;

/// <summary>
/// Extrato do cartão de crédito ActivoBank (PDF, 2 layouts). Valida os totais
/// do "RESUMO DE MOVIMENTOS" e o encadeamento da dívida (D3: tudo-ou-nada).
/// </summary>
public sealed class ActivoBankCardPdfConverter(ILogger<ActivoBankCardPdfConverter> logger) : IStatementConverter
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public StatementFormat Format => StatementFormat.ActivoBankCardPdf;

    public bool CanHandle(string fileName) => fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public StatementConversionResult Convert(Stream stream, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Timeout);

        try
        {
            var pages = PdfStatementTextExtractor.Extract(stream, timeoutCts.Token);
            var result = Build(pages, out var layout);
            logger.LogInformation(
                "Extrato {Format} (layout {Layout}) convertido: {Rows} linhas, {Checks} verificações em {ElapsedMs} ms",
                Format, layout, result.Rows.Count, result.Checks.Count, watch.ElapsedMilliseconds);
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

    internal static StatementConversionResult Build(IReadOnlyList<PdfPageContent> pages, out CardLayout layout)
    {
        var header = CardStatementHeaderParser.Parse(pages);
        layout = CardLayoutDetector.Detect(pages);
        var movements = layout == CardLayout.A
            ? CardLayoutAParser.Parse(pages)
            : CardLayoutBParser.Parse(pages, header.PeriodEnd);

        var summary = header.Summary;
        var debits = movements.Where(m => !m.IsCredit).Sum(m => m.Amount);
        var credits = movements.Where(m => m.IsCredit).Sum(m => m.Amount);
        var expectedDebt = summary.PreviousDebt + summary.Debits - summary.Credits;

        var checks = new List<StatementCheck>
        {
            Check("Total de débitos = resumo", summary.Debits, debits),
            Check("Total de créditos = resumo", summary.Credits, credits),
            Check("Dívida anterior + débitos − créditos = dívida atual", summary.CurrentDebt, expectedDebt),
        };

        var failed = checks.Where(c => !c.Passed).ToList();
        if (failed.Count > 0)
        {
            throw new StatementValidationException(
                "Os totais do extrato não batem com o \"RESUMO DE MOVIMENTOS\": " +
                string.Join("; ", failed.Select(c => $"{c.Name} (esperado {c.Expected}, calculado {c.Actual})")) + ".");
        }

        if (movements.Count == 0)
            throw new StatementValidationException("O extrato não tem movimentos.");

        var rows = movements
            .Select(m => new StatementRow(
                m.Date,
                m.ValueDate,
                m.Description,
                m.IsCredit ? m.Amount : -m.Amount,
                null))
            .ToList();

        return new StatementConversionResult(
            StatementFormat.ActivoBankCardPdf,
            rows,
            -summary.PreviousDebt,
            -summary.CurrentDebt,
            header.PeriodStart,
            header.PeriodEnd,
            checks,
            header.Currency,
            header.CardHint);
    }

    /// <summary>"Esperado" é o valor do resumo do banco; "calculado" o que se extraiu/derivou.</summary>
    private static StatementCheck Check(string name, decimal expected, decimal actual)
        => new(name, expected == actual, StatementText.Amount(expected), StatementText.Amount(actual));
}
