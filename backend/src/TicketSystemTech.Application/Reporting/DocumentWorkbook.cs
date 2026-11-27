using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace TicketSystemTech.Application.Reporting;

/// <summary>A table in a sheet: a row of headings and rows of cells. A cell is text, a number (<see cref="int"/>, <see cref="long"/>, <see cref="double"/>) or nothing.</summary>
public sealed record DocTable(string[] Headers, List<object?[]> Rows, bool LastRowIsTotal = false);

/// <summary>A sheet: a header (label and value lines: who made the document, when, for what), then tables one below the other.</summary>
public sealed record DocSheet(string Name, List<(string Label, string Value)> Header, List<DocTable> Tables, int[]? ColumnWidths = null);

/// <summary>
/// Builds an xlsx workbook out of sheets of header lines and tables. The wording is already in the language of the person who asked: this knows no
/// language. A number is written as a number (hours with two decimals), so that it can be added up in Excel.
/// </summary>
public static class DocumentWorkbook
{
    public static byte[] Build(IReadOnlyList<DocSheet> sheets)
    {
        using var workbook = new XSSFWorkbook();
        var bold = workbook.CreateFont();
        bold.IsBold = true;
        var boldStyle = workbook.CreateCellStyle();
        boldStyle.SetFont(bold);
        var headerStyle = workbook.CreateCellStyle();
        headerStyle.SetFont(bold);
        headerStyle.FillForegroundColor = IndexedColors.Grey25Percent.Index;
        headerStyle.FillPattern = FillPattern.SolidForeground;
        var numberFormat = workbook.CreateDataFormat();
        var hoursStyle = workbook.CreateCellStyle();
        hoursStyle.DataFormat = numberFormat.GetFormat(WorkTimeSummary.HoursNumberFormat);
        var boldHoursStyle = workbook.CreateCellStyle();
        boldHoursStyle.DataFormat = numberFormat.GetFormat(WorkTimeSummary.HoursNumberFormat);
        boldHoursStyle.SetFont(bold);
        var wrapStyle = workbook.CreateCellStyle();
        wrapStyle.WrapText = true;
        wrapStyle.VerticalAlignment = VerticalAlignment.Top;

        foreach (var def in sheets)
        {
            var sheet = workbook.CreateSheet(SafeName(def.Name));
            var r = 0;
            foreach (var (label, value) in def.Header)
            {
                var row = sheet.CreateRow(r++);
                var l = row.CreateCell(0);
                l.SetCellValue(label);
                l.CellStyle = boldStyle;
                if (value.Length > 0) row.CreateCell(1).SetCellValue(value);
            }
            r++;

            foreach (var table in def.Tables)
            {
                var head = sheet.CreateRow(r++);
                for (var c = 0; c < table.Headers.Length; c++)
                {
                    var cell = head.CreateCell(c);
                    cell.SetCellValue(table.Headers[c]);
                    cell.CellStyle = headerStyle;
                }
                for (var i = 0; i < table.Rows.Count; i++)
                {
                    var isTotal = table.LastRowIsTotal && i == table.Rows.Count - 1;
                    var row = sheet.CreateRow(r++);
                    for (var c = 0; c < table.Rows[i].Length; c++)
                    {
                        var value = table.Rows[i][c];
                        switch (value)
                        {
                            case null: break;
                            case double d:
                                var n = row.CreateCell(c, CellType.Numeric);
                                n.SetCellValue(d);
                                n.CellStyle = isTotal ? boldHoursStyle : hoursStyle;
                                break;
                            case int or long:
                                var w = row.CreateCell(c, CellType.Numeric);
                                w.SetCellValue(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                                if (isTotal) w.CellStyle = boldStyle;
                                break;
                            default:
                                var t = row.CreateCell(c);
                                t.SetCellValue(value.ToString());
                                if (isTotal) t.CellStyle = boldStyle;
                                break;
                        }
                    }
                }
                r++;
            }

            // Fixed widths: measuring text needs a font library the application does not ship.
            var widths = def.ColumnWidths ?? new[] { 34, 26, 26, 26, 22, 18, 18 };
            for (var c = 0; c < widths.Length; c++) sheet.SetColumnWidth(c, widths[c] * 256);
        }

        using var ms = new MemoryStream();
        workbook.Write(ms, leaveOpen: true);
        return ms.ToArray();
    }

    /// <summary>Excel allows 31 characters and not : \ / ? * [ ].</summary>
    private static string SafeName(string name)
    {
        var cleaned = new string(name.Where(c => !":\\/?*[]".Contains(c)).ToArray());
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }
}
