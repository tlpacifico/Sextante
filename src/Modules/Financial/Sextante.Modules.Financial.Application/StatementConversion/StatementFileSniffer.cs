namespace Sextante.Modules.Financial.Application.StatementConversion;

/// <summary>
/// Confirma que o conteúdo do ficheiro é do tipo que a extensão diz (assinatura
/// <c>PK</c> / <c>%PDF</c> / JSON a abrir com <c>{</c>); a extensão sozinha não chega
/// para entrada não confiável (Phase 6.6, 5.1).
/// </summary>
public static class StatementFileSniffer
{
    public static readonly IReadOnlyList<string> SupportedExtensions = [".csv", ".xlsx", ".pdf", ".json"];

    public static bool IsStatementFile(string fileName)
        => HasExtension(fileName, ".xlsx") || HasExtension(fileName, ".pdf") || HasExtension(fileName, ".json");

    /// <summary><c>true</c> se o início do ficheiro é compatível com a extensão de <paramref name="fileName"/>.</summary>
    public static bool ContentMatchesExtension(string fileName, ReadOnlySpan<byte> head)
    {
        if (HasExtension(fileName, ".xlsx"))
            return head.Length >= 4 && head[0] == (byte)'P' && head[1] == (byte)'K';

        if (HasExtension(fileName, ".pdf"))
            return head.Length >= 4 && head[..4].SequenceEqual("%PDF"u8);

        if (HasExtension(fileName, ".json"))
        {
            if (head.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
                head = head[3..];
            foreach (var b in head)
            {
                if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
                return b == (byte)'{';
            }

            return false;
        }

        return true;
    }

    private static bool HasExtension(string fileName, string extension)
        => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
}
