namespace Sextante.Modules.Financial.Application.StatementConversion;

/// <summary>
/// Converte um extrato (XLSX, PDF, JSON) num <see cref="StatementConversionResult"/>
/// validado contra os números do próprio banco. Falha tudo-ou-nada: uma
/// verificação que não bate lança <c>StatementValidationException</c>.
/// </summary>
public interface IStatementConverter
{
    StatementFormat Format { get; }

    bool CanHandle(string fileName);

    StatementConversionResult Convert(Stream stream, CancellationToken ct);
}
