using System.Globalization;

namespace Sextante.Modules.Financial.Application.StatementConversion;

/// <summary>Formatação PT-PT das mensagens de validação (vão para o utilizador, não para logs).</summary>
public static class StatementText
{
    public static string Amount(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    public static string Date(DateOnly value)
        => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
