using System.Globalization;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace TicketSystemTech.Application.Reporting;

public sealed record CompanyExportRow(string Company, int Total, int New, int Open, int Closed, double AvgResolutionHours, double? SlaComplianceRate, int PreviousTotal, double SharePercent);

public sealed record ClientExportRow(string Client, int Total, int Open, int Closed);

public sealed record NamedCountExportRow(string Name, int Count);

public sealed record CloserExportRow(string Person, int Closed);

/// <summary>What the export of the company statistics holds: the same figures that the screen shows for the same choice.</summary>
public sealed record CompanyReportExport(
    string Scope,
    DateTime From,
    DateTime To,
    List<CompanyExportRow> Companies,
    List<ClientExportRow> Clients,
    List<NamedCountExportRow> Topics,
    List<NamedCountExportRow> Categories,
    List<NamedCountExportRow> Branches,
    List<CloserExportRow> Closers);

/// <summary>
/// The workbook of the company statistics: an "About" sheet that says what was asked for and what each group of tickets is, then one sheet for each of the lists
/// that have something in them. Sheet and column names are English, like the other export of the application.
/// </summary>
public static class CompanyReportExcelExporter
{
    private static readonly string[] CompanyHeaders = { "Company", "Tickets created", "New", "Open", "Closed", "Avg resolution (h)", "SLA compliance (%)", "Previous period", "Share (%)" };
    private static readonly string[] ClientHeaders = { "Client", "Tickets created", "Open", "Closed" };
    private static readonly string[] TopicHeaders = { "Topic", "Tickets created" };
    private static readonly string[] CategoryHeaders = { "Category", "Tickets created" };
    private static readonly string[] BranchHeaders = { "Branch", "Tickets created" };
    private static readonly string[] CloserHeaders = { "Person", "Tickets closed" };

    public static byte[] Build(CompanyReportExport model)
    {
        using var workbook = new XSSFWorkbook();
        var header = HeaderStyle(workbook);

        var about = workbook.CreateSheet("About");
        var lines = new[]
        {
            ("Scope", model.Scope),
            ("From (UTC)", model.From.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            ("To (UTC)", model.To.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            ("Companies, Clients, Topics, Categories, Branches", "Tickets CREATED in the period (what came in, and how it stands now)."),
            ("Resolution hours, SLA", "Of those, the tickets that are closed by now (opened to closed; closed on or before the due date)."),
            ("Closed by", "Tickets CLOSED in the period, whenever they were created, counted for the person who closed them."),
            ("Previous period", "The period of the same length right before this one."),
            ("Empty names", "(none) is a ticket that has not been given that yet: topic, category, branch and sub-branch are filled in when staff open the ticket.")
        };
        for (var i = 0; i < lines.Length; i++)
        {
            var row = about.CreateRow(i);
            var key = row.CreateCell(0, CellType.String);
            key.SetCellValue(lines[i].Item1);
            key.CellStyle = header;
            row.CreateCell(1, CellType.String).SetCellValue(lines[i].Item2);
        }
        about.SetColumnWidth(0, 50 * 256);
        about.SetColumnWidth(1, 110 * 256);

        if (model.Companies.Count > 0)
            Sheet(workbook, "Companies", header,
                CompanyHeaders,
                model.Companies.Select(c => new object?[] { c.Company, c.Total, c.New, c.Open, c.Closed, c.AvgResolutionHours, c.SlaComplianceRate, c.PreviousTotal, c.SharePercent }));
        if (model.Clients.Count > 0)
            Sheet(workbook, "Clients", header, ClientHeaders, model.Clients.Select(c => new object?[] { c.Client, c.Total, c.Open, c.Closed }));
        if (model.Topics.Count > 0)
            Sheet(workbook, "Topics", header, TopicHeaders, model.Topics.Select(c => new object?[] { c.Name, c.Count }));
        if (model.Categories.Count > 0)
            Sheet(workbook, "Categories", header, CategoryHeaders, model.Categories.Select(c => new object?[] { c.Name, c.Count }));
        if (model.Branches.Count > 0)
            Sheet(workbook, "Branches", header, BranchHeaders, model.Branches.Select(c => new object?[] { c.Name, c.Count }));
        if (model.Closers.Count > 0)
            Sheet(workbook, "Closed by", header, CloserHeaders, model.Closers.Select(c => new object?[] { c.Person, c.Closed }));

        using var ms = new MemoryStream();
        workbook.Write(ms, leaveOpen: true);
        return ms.ToArray();
    }

    private static void Sheet(XSSFWorkbook workbook, string name, ICellStyle header, string[] headers, IEnumerable<object?[]> rows)
    {
        var sheet = workbook.CreateSheet(name);
        var head = sheet.CreateRow(0);
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = head.CreateCell(i, CellType.String);
            cell.SetCellValue(headers[i]);
            cell.CellStyle = header;
        }

        var r = 1;
        foreach (var values in rows)
        {
            var row = sheet.CreateRow(r++);
            for (var c = 0; c < values.Length; c++)
            {
                switch (values[c])
                {
                    case null: break; // no data: an empty cell, not a zero
                    case string s: row.CreateCell(c, CellType.String).SetCellValue(s); break;
                    case int n: row.CreateCell(c, CellType.Numeric).SetCellValue(n); break;
                    case double d: row.CreateCell(c, CellType.Numeric).SetCellValue(d); break;
                }
            }
        }

        for (var i = 0; i < headers.Length; i++) sheet.SetColumnWidth(i, (i == 0 ? 42 : 20) * 256);
    }

    private static ICellStyle HeaderStyle(XSSFWorkbook workbook)
    {
        var style = workbook.CreateCellStyle();
        var font = workbook.CreateFont();
        font.IsBold = true;
        style.SetFont(font);
        return style;
    }
}
