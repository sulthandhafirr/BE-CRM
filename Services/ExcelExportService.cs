using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace CRM.Api.Services
{
    /// <summary>Describes a single sheet inside an Excel workbook.</summary>
    public record WorkbookSheet(string SheetName, string[] Headers, List<object?[]> Rows);

    /// <summary>
    /// Minimal .xlsx writer built on <see cref="System.IO.Compression"/> so no
    /// third-party package is required. Produces a valid SpreadsheetML workbook
    /// (shared strings, styled header row, auto column widths) that opens in
    /// Excel, LibreOffice, and Google Sheets.
    /// </summary>
    public class ExcelExportService
    {
        public byte[] BuildWorkbook(params WorkbookSheet[] sheets)
        {
            // ── Collect shared strings (all text cells) ──────────────────
            var strings = new List<string>();
            var stringIndex = new Dictionary<string, int>(StringComparer.Ordinal);

            int GetStringIndex(string value)
            {
                if (stringIndex.TryGetValue(value, out var existing)) return existing;
                stringIndex[value] = strings.Count;
                strings.Add(value);
                return strings.Count - 1;
            }

            foreach (var sheet in sheets)
            {
                foreach (var row in sheet.Rows)
                {
                    foreach (var cell in row)
                    {
                        if (cell is string text) GetStringIndex(text);
                    }
                }
            }

            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                WriteEntry(archive, "[Content_Types].xml", BuildContentTypes(sheets.Length));
                WriteEntry(archive, "_rels/.rels", BuildRootRels());
                WriteEntry(archive, "xl/workbook.xml", BuildWorkbookXml(sheets));
                WriteEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets.Length));
                for (var i = 0; i < sheets.Length; i++)
                {
                    WriteEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildSheetXml(sheets[i], GetStringIndex));
                }
                WriteEntry(archive, "xl/sharedStrings.xml", BuildSharedStringsXml(strings));
                WriteEntry(archive, "xl/styles.xml", BuildStylesXml());
            }

            return ms.ToArray();
        }

        // ── Entry writers ────────────────────────────────────────────────

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string BuildContentTypes(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            for (var i = 1; i <= sheetCount; i++)
            {
                sb.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
            sb.Append("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
            sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string BuildRootRels()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>";
        }

        private static string BuildWorkbookXml(WorkbookSheet[] sheets)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<sheets>");
            for (var i = 0; i < sheets.Length; i++)
            {
                sb.Append($"<sheet name=\"{XmlEncode(sheets[i].SheetName)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            }
            sb.Append("</sheets></workbook>");
            return sb.ToString();
        }

        private static string BuildWorkbookRels(int sheetCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (var i = 1; i <= sheetCount; i++)
            {
                sb.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
            }
            sb.Append("<Relationship Id=\"rIdStrings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>");
            sb.Append("<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private static string BuildSheetXml(WorkbookSheet sheet, Func<string, int> getStringIndex)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            // ── Column widths (based on header + longest cell) ──────────
            if (sheet.Headers.Length > 0)
            {
                sb.Append("<cols>");
                for (var c = 0; c < sheet.Headers.Length; c++)
                {
                    var maxLen = sheet.Headers[c].Length;
                    foreach (var row in sheet.Rows)
                    {
                        var value = c < row.Length ? row[c] : null;
                        if (value != null)
                        {
                            var len = value.ToString()!.Length;
                            if (len > maxLen) maxLen = len;
                        }
                    }
                    var width = Math.Clamp(maxLen + 2, 8, 60);
                    sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width}\" customWidth=\"1\"/>");
                }
                sb.Append("</cols>");
            }

            sb.Append("<sheetData>");

            // ── Header row (style 1 = bold white on accent) ─────────────
            sb.Append("<row r=\"1\">");
            for (var c = 0; c < sheet.Headers.Length; c++)
            {
                var index = getStringIndex(sheet.Headers[c]);
                sb.Append($"<c r=\"{ColumnName(c)}1\" t=\"s\" s=\"1\"><v>{index}</v></c>");
            }
            sb.Append("</row>");

            // ── Data rows ───────────────────────────────────────────────
            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                var rowNumber = r + 2;
                sb.Append($"<row r=\"{rowNumber}\">");
                var row = sheet.Rows[r];
                for (var c = 0; c < row.Length; c++)
                {
                    var value = row[c];
                    if (value is null) continue;

                    var cellRef = $"{ColumnName(c)}{rowNumber}";
                    if (value is string text)
                    {
                        var index = getStringIndex(text);
                        sb.Append($"<c r=\"{cellRef}\" t=\"s\"><v>{index}</v></c>");
                    }
                    else
                    {
                        // Numeric values (long / int / double / decimal / bool)
                        var numeric = Convert.ToString(value, CultureInfo.InvariantCulture);
                        sb.Append($"<c r=\"{cellRef}\"><v>{numeric}</v></c>");
                    }
                }
                sb.Append("</row>");
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string BuildSharedStringsXml(List<string> strings)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append($"<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{strings.Count}\" uniqueCount=\"{strings.Count}\">");
            foreach (var s in strings)
            {
                sb.Append($"<si><t xml:space=\"preserve\">{XmlEncode(s)}</t></si>");
            }
            sb.Append("</sst>");
            return sb.ToString();
        }

        private static string BuildStylesXml()
        {
            return """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="2">
                    <font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/></font>
                    <font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/><family val="2"/></font>
                  </fonts>
                  <fills count="3">
                    <fill><patternFill patternType="none"/></fill>
                    <fill><patternFill patternType="gray125"/></fill>
                    <fill><patternFill patternType="solid"><fgColor rgb="FFFF7A33"/><bgColor indexed="64"/></patternFill></fill>
                  </fills>
                  <borders count="1">
                    <border><left/><right/><top/><bottom/><diagonal/></border>
                  </borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="2">
                    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                    <xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/>
                  </cellXfs>
                  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
                  <dxfs count="0"/>
                  <tableStyles count="0" defaultTableStyle="TableStyleMedium9" defaultPivotStyle="PivotStyleLight16"/>
                </styleSheet>
                """;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>0-based column index → Excel column letters (A, B, …, Z, AA, …).</summary>
        private static string ColumnName(int index)
        {
            var name = "";
            for (var i = index; i >= 0; i = i / 26 - 1)
            {
                name = (char)('A' + i % 26) + name;
            }
            return name;
        }

        private static string XmlEncode(string value)
            => value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
    }
}
