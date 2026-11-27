using System.Globalization;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using TicketSystemTech.Application.Sla;

namespace TicketSystemTech.Application.Reporting;

/// <summary>Everything the worksheet of one person for one month holds. The wording comes in through <see cref="T"/>, in the language of the person who asked.</summary>
public sealed record TimesheetDocument(
    string PersonName,
    int Year,
    int Month,
    string MadeBy,
    DateTime MadeAtUtc,
    TimeZoneInfo Zone,
    List<DayWork> Days,
    List<GroupWork> Companies,
    List<GroupWork> Items,
    List<WorkEntry> Entries,
    Func<string, string> T);

/// <summary>
/// The monthly worksheet of a person (xlsx): hours by day, by company, by ticket or task, and the raw list of sessions. Four sheets, each with the same header:
/// who made it, when, for whom and for what month, and that it is the state at that moment (sessions can still be corrected later).
/// </summary>
public static class TimesheetExcelExporter
{
    public static byte[] Build(TimesheetDocument doc)
    {
        using var workbook = new XSSFWorkbook();
        var styles = new Styles(workbook);

        var total = doc.Days.Sum(d => d.TotalSeconds);
        var ticketTotal = doc.Days.Sum(d => d.TicketSeconds);
        var taskTotal = doc.Days.Sum(d => d.TaskSeconds);

        // 1. by day
        var days = Sheet(workbook, doc, styles, doc.T("ts_sheetDays"));
        var r = Row(days.Sheet, days.Next, styles.Header, doc.T("ts_day"), doc.T("ts_ticketHours"), doc.T("ts_taskHours"), doc.T("ts_totalHours"), doc.T("ts_runningCol"));
        foreach (var d in doc.Days)
        {
            r = Numbers(days.Sheet, r, styles, d.Day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + " " + doc.T("ts_wd" + (int)d.Day.DayOfWeek),
                new[] { WorkTimeSummary.Hours(d.TicketSeconds), WorkTimeSummary.Hours(d.TaskSeconds), WorkTimeSummary.Hours(d.TotalSeconds) });
            Count(days.Sheet.GetRow(r - 1), 4, d.RunningCount);
        }
        Numbers(days.Sheet, r, styles, doc.T("ts_total"), new[] { WorkTimeSummary.Hours(ticketTotal), WorkTimeSummary.Hours(taskTotal), WorkTimeSummary.Hours(total) }, bold: true);
        Fit(days.Sheet, 5);

        // 2. by company
        var companies = Sheet(workbook, doc, styles, doc.T("ts_sheetCompanies"));
        r = Row(companies.Sheet, companies.Next, styles.Header, doc.T("ts_company"), doc.T("ts_totalHours"), doc.T("ts_runningCol"));
        foreach (var c in doc.Companies)
        {
            r = Numbers(companies.Sheet, r, styles, c.Label, new[] { WorkTimeSummary.Hours(c.Seconds) });
            Count(companies.Sheet.GetRow(r - 1), 2, c.RunningCount);
        }
        Numbers(companies.Sheet, r, styles, doc.T("ts_total"), new[] { WorkTimeSummary.Hours(total) }, bold: true);
        Fit(companies.Sheet, 3);

        // 3. by ticket or task
        var items = Sheet(workbook, doc, styles, doc.T("ts_sheetItems"));
        r = Row(items.Sheet, items.Next, styles.Header, doc.T("ts_kind"), doc.T("ts_item"), doc.T("ts_totalHours"));
        foreach (var i in doc.Items)
        {
            var row = items.Sheet.CreateRow(r++);
            row.CreateCell(0).SetCellValue(i.IsTask ? doc.T("ts_task") : doc.T("ts_ticket"));
            row.CreateCell(1).SetCellValue(i.RunningCount > 0 ? $"{i.Label} ({doc.T("ts_running")})" : i.Label);
            Hours(row, 2, WorkTimeSummary.Hours(i.Seconds), styles.Hours);
        }
        var totalRow = items.Sheet.CreateRow(r);
        totalRow.CreateCell(0).SetCellValue(doc.T("ts_total"));
        Hours(totalRow, 2, WorkTimeSummary.Hours(total), styles.BoldHours);
        totalRow.GetCell(0).CellStyle = styles.Bold;
        Fit(items.Sheet, 3);

        // 4. the raw list
        var raw = Sheet(workbook, doc, styles, doc.T("ts_sheetRaw"));
        r = Row(raw.Sheet, raw.Next, styles.Header, doc.T("ts_start"), doc.T("ts_end"), doc.T("ts_kind"), doc.T("ts_item"), doc.T("ts_company"), doc.T("ts_totalHours"));
        foreach (var e in doc.Entries.OrderBy(e => e.StartUtc))
        {
            var row = raw.Sheet.CreateRow(r++);
            row.CreateCell(0).SetCellValue(Local(e.StartUtc, doc.Zone));
            row.CreateCell(1).SetCellValue(e.EndUtc is { } end ? Local(end, doc.Zone) : doc.T("ts_running"));
            row.CreateCell(2).SetCellValue(e.IsTask ? doc.T("ts_task") : doc.T("ts_ticket"));
            row.CreateCell(3).SetCellValue(e.IsTask ? e.ItemTitle : $"{e.ItemNumber} {e.ItemTitle}".Trim());
            row.CreateCell(4).SetCellValue(e.IsTask ? doc.T("ts_tasksNoCompany") : e.CompanyName ?? "");
            Hours(row, 5, WorkTimeSummary.Hours(WorkTimeSummary.Seconds(e, doc.MadeAtUtc)), styles.Hours);
        }
        Fit(raw.Sheet, 6);

        using var ms = new MemoryStream();
        workbook.Write(ms, leaveOpen: true);
        return ms.ToArray();
    }

    private static string Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(BusinessTimeZone.AsUtc(utc), zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private sealed class Styles
    {
        public Styles(XSSFWorkbook workbook)
        {
            var bold = workbook.CreateFont();
            bold.IsBold = true;
            Bold = workbook.CreateCellStyle();
            Bold.SetFont(bold);
            Header = workbook.CreateCellStyle();
            Header.SetFont(bold);
            Header.FillForegroundColor = IndexedColors.Grey25Percent.Index;
            Header.FillPattern = FillPattern.SolidForeground;
            var format = workbook.CreateDataFormat().GetFormat(WorkTimeSummary.HoursNumberFormat);
            Hours = workbook.CreateCellStyle();
            Hours.DataFormat = format;
            BoldHours = workbook.CreateCellStyle();
            BoldHours.DataFormat = format;
            BoldHours.SetFont(bold);
        }

        public ICellStyle Bold { get; }
        public ICellStyle Header { get; }
        public ICellStyle Hours { get; }
        public ICellStyle BoldHours { get; }
    }

    /// <summary>A sheet with the header of the document; <c>Next</c> is the first row below it.</summary>
    private static (ISheet Sheet, int Next) Sheet(XSSFWorkbook workbook, TimesheetDocument doc, Styles styles, string name)
    {
        var sheet = workbook.CreateSheet(name);
        var month = $"{doc.Month:00}.{doc.Year}";
        var madeAt = Local(doc.MadeAtUtc, doc.Zone) + " (" + doc.Zone.Id + ")";
        var lines = new List<(string Label, string Value)>
        {
            (doc.T("ts_title"), ""),
            (doc.T("ts_person"), doc.PersonName),
            (doc.T("ts_month"), month),
            (doc.T("ts_madeBy"), doc.MadeBy),
            (doc.T("ts_madeAt"), madeAt),
            (doc.T("ts_snapshotLabel"), doc.T("ts_snapshot")),
        };
        var running = WorkTimeSummary.RunningCount(doc.Entries);
        // A session that has not ended is counted up to now, so its hours are not final: the sheet says so, on every sheet.
        if (running > 0) lines.Add((doc.T("ts_runningLabel"), string.Format(CultureInfo.InvariantCulture, doc.T("ts_runningWarning"), running, madeAt)));
        for (var i = 0; i < lines.Count; i++)
        {
            var row = sheet.CreateRow(i);
            var label = row.CreateCell(0);
            label.SetCellValue(lines[i].Label);
            label.CellStyle = styles.Bold;
            if (lines[i].Value.Length > 0) row.CreateCell(1).SetCellValue(lines[i].Value);
        }
        return (sheet, lines.Count + 1);
    }

    private static int Row(ISheet sheet, int rowIndex, ICellStyle style, params string[] cells)
    {
        var row = sheet.CreateRow(rowIndex);
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = row.CreateCell(i);
            cell.SetCellValue(cells[i]);
            cell.CellStyle = style;
        }
        return rowIndex + 1;
    }

    private static int Numbers(ISheet sheet, int rowIndex, Styles styles, string label, double[] hours, bool bold = false)
    {
        var row = sheet.CreateRow(rowIndex);
        var cell = row.CreateCell(0);
        cell.SetCellValue(label);
        if (bold) cell.CellStyle = styles.Bold;
        for (var i = 0; i < hours.Length; i++) Hours(row, i + 1, hours[i], bold ? styles.BoldHours : styles.Hours);
        return rowIndex + 1;
    }

    /// <summary>A whole number in a cell, only when there is something to say (a zero is left blank).</summary>
    private static void Count(IRow row, int column, int count)
    {
        if (count <= 0) return;
        row.CreateCell(column, CellType.Numeric).SetCellValue(count);
    }

    private static void Hours(IRow row, int column, double hours, ICellStyle style)
    {
        var cell = row.CreateCell(column, CellType.Numeric);
        cell.SetCellValue(hours);
        cell.CellStyle = style;
    }

    /// <summary>Column widths in characters. Fixed on purpose: measuring the text needs a font library the application does not ship.</summary>
    private static void Fit(ISheet sheet, int columns)
    {
        var widths = new[] { 36, 22, 22, 40, 30, 12 };
        for (var i = 0; i < columns; i++) sheet.SetColumnWidth(i, widths[i] * 256);
    }
}
