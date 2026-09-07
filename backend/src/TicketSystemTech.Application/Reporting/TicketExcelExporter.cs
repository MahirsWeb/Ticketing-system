using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XDDF.UserModel.Chart;
using NPOI.XSSF.UserModel;

namespace TicketSystemTech.Application.Reporting;

public record TicketExportRow(
    string TicketNumber,
    string Title,
    string Status,
    string Branch,
    string Company,
    string Client,
    string? AssignedTo,
    DateTime CreatedAtUtc,
    DateTime? OpenedAtUtc,
    DateTime? ClosedAtUtc,
    double? ResolutionHours,
    string? ResolvedBy
);

/// <summary>One ticket or task work block for one Employee — the data-table form of a Gantt chart bar.</summary>
public record TimelineExportRow(
    string Employee,
    string Type, // "Ticket" | "Task"
    string Title,
    string Status,
    DateTime? StartUtc,
    DateTime? EndUtc,
    double? DurationHours
);

/// <summary>Builds a two-sheet Excel workbook (raw ticket data + a summary with a monthly bar chart) for the reports export.</summary>
public static class TicketExcelExporter
{
    public static byte[] Build(List<TicketExportRow> rows, DateTime from, DateTime to, List<TimelineExportRow>? timelineRows = null)
    {
        using var workbook = new XSSFWorkbook();
        var headerStyle = CreateHeaderStyle(workbook);
        var dateStyle = CreateDateStyle(workbook);

        BuildTicketsSheet(workbook, rows, headerStyle, dateStyle);
        BuildSummarySheet(workbook, rows, from, to, headerStyle);
        if (timelineRows is { Count: > 0 }) BuildTimelineSheet(workbook, timelineRows, headerStyle, dateStyle);

        using var ms = new MemoryStream();
        workbook.Write(ms, leaveOpen: true);
        return ms.ToArray();
    }

    private static void BuildTicketsSheet(XSSFWorkbook workbook, List<TicketExportRow> rows, ICellStyle headerStyle, ICellStyle dateStyle)
    {
        var sheet = workbook.CreateSheet("Tickets");
        string[] headers =
        {
            "Ticket Number", "Title", "Status", "Branch", "Company", "Client", "Assigned To",
            "Created At (UTC)", "Opened At (UTC)", "Closed At (UTC)", "Resolution Hours", "Resolved By"
        };

        var headerRow = sheet.CreateRow(0);
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = headerRow.CreateCell(i, CellType.String);
            cell.SetCellValue(headers[i]);
            cell.CellStyle = headerStyle;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var excelRow = sheet.CreateRow(r + 1);
            excelRow.CreateCell(0).SetCellValue(row.TicketNumber);
            excelRow.CreateCell(1).SetCellValue(row.Title);
            excelRow.CreateCell(2).SetCellValue(row.Status);
            excelRow.CreateCell(3).SetCellValue(row.Branch);
            excelRow.CreateCell(4).SetCellValue(row.Company);
            excelRow.CreateCell(5).SetCellValue(row.Client);
            excelRow.CreateCell(6).SetCellValue(row.AssignedTo ?? "");

            SetDateCell(excelRow.CreateCell(7), row.CreatedAtUtc, dateStyle);
            if (row.OpenedAtUtc.HasValue) SetDateCell(excelRow.CreateCell(8), row.OpenedAtUtc.Value, dateStyle);
            if (row.ClosedAtUtc.HasValue) SetDateCell(excelRow.CreateCell(9), row.ClosedAtUtc.Value, dateStyle);
            if (row.ResolutionHours.HasValue) excelRow.CreateCell(10).SetCellValue(row.ResolutionHours.Value);
            excelRow.CreateCell(11).SetCellValue(row.ResolvedBy ?? "");
        }

        // Fixed widths instead of AutoSizeColumn: NPOI's auto-sizing needs SkiaSharp for font metrics, which
        // isn't reliably available in a headless Linux container — fixed widths avoid that dependency entirely.
        int[] widths = { 14, 40, 12, 16, 24, 22, 22, 18, 18, 18, 14, 22 };
        for (var i = 0; i < headers.Length; i++) sheet.SetColumnWidth(i, widths[i] * 256);
    }

    private static void BuildSummarySheet(XSSFWorkbook workbook, List<TicketExportRow> rows, DateTime from, DateTime to, ICellStyle headerStyle)
    {
        var sheet = workbook.CreateSheet("Summary");

        var title = sheet.CreateRow(0).CreateCell(0);
        title.SetCellValue($"Ticket summary — {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");

        var statsHeaderRow = sheet.CreateRow(2);
        statsHeaderRow.CreateCell(0).SetCellValue("Metric");
        statsHeaderRow.CreateCell(1).SetCellValue("Value");
        statsHeaderRow.GetCell(0).CellStyle = headerStyle;
        statsHeaderRow.GetCell(1).CellStyle = headerStyle;

        var newCount = rows.Count(r => r.Status == "New");
        var openCount = rows.Count(r => r.Status is "Open" or "InProgress");
        var closedCount = rows.Count(r => r.Status == "Closed");
        var avgResolution = rows.Where(r => r.ResolutionHours.HasValue).Select(r => r.ResolutionHours!.Value).DefaultIfEmpty(0).Average();

        var stats = new (string Label, object Value)[]
        {
            ("Total tickets", rows.Count),
            ("New", newCount),
            ("Open / In progress", openCount),
            ("Closed", closedCount),
            ("Avg. resolution (hours)", Math.Round(avgResolution, 1)),
        };
        for (var i = 0; i < stats.Length; i++)
        {
            var row = sheet.CreateRow(3 + i);
            row.CreateCell(0).SetCellValue(stats[i].Label);
            var cell = row.CreateCell(1);
            if (stats[i].Value is int intVal) cell.SetCellValue(intVal);
            else cell.SetCellValue(Convert.ToDouble(stats[i].Value));
        }

        // Monthly breakdown table, used as the data source for the chart below.
        var monthlyHeaderRowIndex = 3 + stats.Length + 2;
        var monthlyHeaderRow = sheet.CreateRow(monthlyHeaderRowIndex);
        monthlyHeaderRow.CreateCell(0).SetCellValue("Month");
        monthlyHeaderRow.CreateCell(1).SetCellValue("Tickets");
        monthlyHeaderRow.GetCell(0).CellStyle = headerStyle;
        monthlyHeaderRow.GetCell(1).CellStyle = headerStyle;

        var monthly = rows
            .GroupBy(r => new DateTime(r.CreatedAtUtc.Year, r.CreatedAtUtc.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => (Month: g.Key, Count: g.Count()))
            .ToList();

        var monthlyDataStartRow = monthlyHeaderRowIndex + 1;
        for (var i = 0; i < monthly.Count; i++)
        {
            var row = sheet.CreateRow(monthlyDataStartRow + i);
            row.CreateCell(0).SetCellValue(monthly[i].Month.ToString("MMM yyyy"));
            row.CreateCell(1).SetCellValue(monthly[i].Count);
        }

        sheet.SetColumnWidth(0, 24 * 256);
        sheet.SetColumnWidth(1, 14 * 256);

        if (monthly.Count > 0)
        {
            TryAddMonthlyChart((XSSFSheet)sheet, monthlyDataStartRow, monthlyDataStartRow + monthly.Count - 1);
        }
    }

    /// <summary>
    /// Data-table equivalent of the Dashboard's Gantt chart, for every Employee in scope across the whole
    /// exported period — one row per ticket/task work block, sorted by employee then start time.
    /// </summary>
    private static void BuildTimelineSheet(XSSFWorkbook workbook, List<TimelineExportRow> rows, ICellStyle headerStyle, ICellStyle dateStyle)
    {
        var sheet = workbook.CreateSheet("Timeline");
        string[] headers = { "Employee", "Type", "Title", "Status", "Started (UTC)", "Ended (UTC)", "Duration (hours)" };

        var headerRow = sheet.CreateRow(0);
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = headerRow.CreateCell(i, CellType.String);
            cell.SetCellValue(headers[i]);
            cell.CellStyle = headerStyle;
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var excelRow = sheet.CreateRow(r + 1);
            excelRow.CreateCell(0).SetCellValue(row.Employee);
            excelRow.CreateCell(1).SetCellValue(row.Type);
            excelRow.CreateCell(2).SetCellValue(row.Title);
            excelRow.CreateCell(3).SetCellValue(row.Status);
            if (row.StartUtc.HasValue) SetDateCell(excelRow.CreateCell(4), row.StartUtc.Value, dateStyle);
            if (row.EndUtc.HasValue) SetDateCell(excelRow.CreateCell(5), row.EndUtc.Value, dateStyle);
            if (row.DurationHours.HasValue) excelRow.CreateCell(6).SetCellValue(row.DurationHours.Value);
        }

        int[] widths = { 22, 10, 40, 12, 18, 18, 16 };
        for (var i = 0; i < headers.Length; i++) sheet.SetColumnWidth(i, widths[i] * 256);
    }

    /// <summary>Chart generation is best-effort: if the NPOI charting API misbehaves for any reason, the data tables above still stand on their own.</summary>
    private static void TryAddMonthlyChart(XSSFSheet sheet, int dataStartRow, int dataEndRow)
    {
        try
        {
            var drawing = (XSSFDrawing)sheet.CreateDrawingPatriarch();
            var anchor = drawing.CreateAnchor(0, 0, 0, 0, 3, 2, 12, 22);
            var chart = drawing.CreateChart(anchor);
            chart.SetTitleText("Tickets per month");
            chart.GetOrAddLegend();

            var categoryAxis = chart.CreateCategoryAxis(AxisPosition.Bottom);
            var valueAxis = chart.CreateValueAxis(AxisPosition.Left);

            var categories = XDDFDataSourcesFactory.FromStringCellRange(sheet, new CellRangeAddress(dataStartRow, dataEndRow, 0, 0));
            var values = XDDFDataSourcesFactory.FromNumericCellRange(sheet, new CellRangeAddress(dataStartRow, dataEndRow, 1, 1));

            var barChartData = (XDDFBarChartData<string, double>)chart.CreateData<string, double>(ChartTypes.BAR, categoryAxis, valueAxis);
            barChartData.BarDirection = BarDirection.Col;
            var series = barChartData.AddSeries(categories, values);
            series.SetTitle("Tickets");

            chart.Plot(barChartData);
        }
        catch
        {
            // Chart is a nice-to-have; the underlying data table remains in the sheet regardless.
        }
    }

    private static void SetDateCell(ICell cell, DateTime value, ICellStyle dateStyle)
    {
        cell.SetCellValue(value);
        cell.CellStyle = dateStyle;
    }

    private static ICellStyle CreateHeaderStyle(XSSFWorkbook workbook)
    {
        var font = workbook.CreateFont();
        font.IsBold = true;
        var style = workbook.CreateCellStyle();
        style.SetFont(font);
        return style;
    }

    private static ICellStyle CreateDateStyle(XSSFWorkbook workbook)
    {
        var style = workbook.CreateCellStyle();
        style.DataFormat = workbook.CreateDataFormat().GetFormat("yyyy-mm-dd hh:mm");
        return style;
    }
}
