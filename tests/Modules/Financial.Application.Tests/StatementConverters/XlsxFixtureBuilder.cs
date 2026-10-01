using System.IO.Compression;
using System.Globalization;
using System.Security;
using System.Text;

namespace Sextante.Modules.Financial.Application.Tests.StatementConverters;

/// <summary>
/// Gera XLSX sintéticos em memória (D15: nunca ficheiros reais no repo). Só o
/// mínimo de OOXML que o leitor precisa: strings inline, números e datas
/// (número de série + estilo com formato de data).
/// </summary>
public static class XlsxFixtureBuilder
{
    public static byte[] Build(IEnumerable<object?[]> rows)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Add(zip, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="Movimentos" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Add(zip, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Add(zip, "xl/styles.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts>
                <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                <cellXfs count="2">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/>
                </cellXfs>
                </styleSheet>
                """);
            Add(zip, "xl/worksheets/sheet1.xml", Sheet(rows));
        }

        return ms.ToArray();
    }

    private static string Sheet(IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        var r = 0;
        foreach (var row in rows)
        {
            r++;
            sb.Append($"<row r=\"{r}\">");
            for (var c = 0; c < row.Length; c++)
            {
                var cell = row[c];
                if (cell is null) continue;
                var reference = $"{(char)('A' + c)}{r}";
                switch (cell)
                {
                    case DateTime dt:
                        sb.Append($"<c r=\"{reference}\" s=\"1\"><v>{(dt - new DateTime(1899, 12, 30)).TotalDays.ToString(CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case string s:
                        sb.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(s)}</t></is></c>");
                        break;
                    default:
                        sb.Append($"<c r=\"{reference}\"><v>{System.Convert.ToString(cell, CultureInfo.InvariantCulture)}</v></c>");
                        break;
                }
            }

            sb.Append("</row>");
        }

        return sb.Append("</sheetData></worksheet>").ToString();
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }
}
