using NPOI.XSSF.UserModel;
using TicketSystemTech.Api.Reports;
using TicketSystemTech.Application.Reporting;
using TicketSystemTech.Application.Sla;

namespace TicketSystemTech.Tests;

/// <summary>Adding up recorded work, and the worksheet made of it.</summary>
public class WorkTimeSummaryTests
{
    private static readonly TimeZoneInfo Zone = BusinessTimeZone.Resolve("Europe/Sarajevo");
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static WorkEntry Ticket(DateTime start, double seconds, string company = "Alfa", string number = "T-1") =>
        new(Guid.NewGuid(), Me, false, Guid.NewGuid(), number, "naslov " + number, CompanyId(company), company, start, start.AddSeconds(seconds));

    private static Guid CompanyId(string name) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(name)));

    private static WorkEntry Task(DateTime start, double seconds, string title = "zadatak") =>
        new(Guid.NewGuid(), Me, true, Guid.NewGuid(), "", title, null, null, start, start.AddSeconds(seconds));

    private static DateTime At(int y, int m, int d, int h = 9, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void A_session_that_is_still_running_counts_up_to_the_moment_the_figure_is_made()
    {
        var running = new WorkEntry(Guid.NewGuid(), Me, false, Guid.NewGuid(), "T-1", "x", null, null, Now.AddHours(-2), null);

        Assert.Equal(7200, WorkTimeSummary.Seconds(running, Now));
        Assert.Equal(0, WorkTimeSummary.Seconds(running, Now.AddHours(-3))); // asked about a moment before it began: nothing, not negative
    }

    [Fact]
    public void A_session_that_ends_before_it_starts_is_nothing()
    {
        var wrong = new WorkEntry(Guid.NewGuid(), Me, false, Guid.NewGuid(), "T-1", "x", null, null, Now, Now.AddMinutes(-5));

        Assert.Equal(0, WorkTimeSummary.Seconds(wrong, Now));
    }

    [Fact]
    public void Hours_are_rounded_once_on_the_sum_and_not_session_by_session()
    {
        var sessions = new[] { Ticket(At(2026, 3, 2), 1500), Ticket(At(2026, 3, 3), 1500), Ticket(At(2026, 3, 4), 1500) }; // 25 min each: 0.42 h when rounded alone

        var total = WorkTimeSummary.Total(sessions, Now);

        Assert.Equal(4500, total);
        Assert.Equal(1.25, WorkTimeSummary.Hours(total)); // rounding each would have given 3 × 0.42 = 1.26
        Assert.Equal(0.42, WorkTimeSummary.Hours(1500));
    }

    [Fact]
    public void A_session_belongs_to_the_local_day_it_started_in_and_is_not_split_at_midnight()
    {
        // 00:30 on 1 February in Sarajevo is still 31 January, 23:30, in UTC; and it runs on past midnight of the next day.
        var s = Ticket(new DateTime(2026, 1, 31, 23, 30, 0, DateTimeKind.Utc), 30 * 3600);

        var days = WorkTimeSummary.ByDay(new[] { s }, new DateOnly(2026, 1, 30), new DateOnly(2026, 2, 3), Zone, Now);

        Assert.Equal(new DateOnly(2026, 2, 1), days.Single(d => d.TotalSeconds > 0).Day);
        Assert.Equal(30 * 3600, days.Sum(d => d.TotalSeconds));
    }

    [Fact]
    public void Every_day_of_the_range_has_a_line_also_the_days_with_no_work_and_tickets_are_apart_from_tasks()
    {
        var days = WorkTimeSummary.ByDay(new[] { Ticket(At(2026, 3, 10), 7200), Task(At(2026, 3, 10, 14), 3600) }, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 11), Zone, Now);

        Assert.Equal(3, days.Count);
        Assert.Equal(0, days[0].TotalSeconds);
        Assert.Equal((7200, 3600), (days[1].TicketSeconds, days[1].TaskSeconds));
        Assert.Equal(0, days[2].TotalSeconds);
    }

    [Fact]
    public void Weeks_run_Monday_to_Sunday()
    {
        var days = WorkTimeSummary.ByDay(new[] { Ticket(At(2026, 3, 8), 3600), Ticket(At(2026, 3, 9), 7200) }, new DateOnly(2026, 3, 7), new DateOnly(2026, 3, 10), Zone, Now); // Sat, Sun, Mon, Tue

        var weeks = WorkTimeSummary.ByWeek(days);

        Assert.Equal(new[] { new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 9) }, weeks.Select(w => w.WeekStart));
        Assert.Equal(new long[] { 3600, 7200 }, weeks.Select(w => w.TotalSeconds));
    }

    [Fact]
    public void Work_on_tasks_is_its_own_group_without_a_company_and_comes_last()
    {
        var groups = WorkTimeSummary.ByCompany(new[] { Task(At(2026, 3, 2), 99999), Ticket(At(2026, 3, 2), 100, "Beta"), Ticket(At(2026, 3, 3), 900, "Alfa"), Ticket(At(2026, 3, 4), 100, "Alfa") }, "Zadaci (bez firme)", Now);

        Assert.Equal(new[] { "Alfa", "Beta", "Zadaci (bez firme)" }, groups.Select(g => g.Label));
        Assert.Equal(1000, groups[0].Seconds);
    }

    [Fact]
    public void The_working_days_with_nothing_recorded_are_Monday_to_Friday_from_the_account_to_today()
    {
        // 1–14 March 2026: Sunday the 1st … Saturday the 14th. Work on Tuesday the 3rd and on Saturday the 7th.
        var days = WorkTimeSummary.ByDay(new[] { Ticket(At(2026, 3, 3), 600), Ticket(At(2026, 3, 7), 600) }, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 14), Zone, Now);

        var all = WorkTimeSummary.DaysWithoutWork(days, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Assert.Equal(new[] { 2, 4, 5, 6, 9, 10, 11, 12, 13 }, all.Select(d => d.Day)); // no weekend, not the 3rd
        Assert.DoesNotContain(new DateOnly(2026, 3, 7), all); // a Saturday with work is no gap, and one without is not a working day

        var fromWednesday = WorkTimeSummary.DaysWithoutWork(days, new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 10)); // account made on the 4th, today the 10th
        Assert.Equal(new[] { 4, 5, 6, 9, 10 }, fromWednesday.Select(d => d.Day));
    }

    // ---- the worksheet

    private static TimesheetDocument Document(string language)
    {
        var entries = new List<WorkEntry> { Ticket(At(2026, 3, 2), 5400, "Alfa", "T-1"), Task(At(2026, 3, 2, 12), 1800), Ticket(At(2026, 3, 3), 1500, "Beta", "T-2"),
            new(Guid.NewGuid(), Me, false, Guid.NewGuid(), "T-3", "traje", CompanyId("Alfa"), "Alfa", At(2026, 3, 4), null) };
        var now = At(2026, 3, 4, 11);
        var days = WorkTimeSummary.ByDay(entries, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), Zone, now);
        return new TimesheetDocument("Amra Hadžić", 2026, 3, "Admin Admin", now, Zone, days,
            WorkTimeSummary.ByCompany(entries, ReportText.For(language)("ts_tasksNoCompany"), now), WorkTimeSummary.ByItem(entries, now), entries, ReportText.For(language));
    }

    [Fact]
    public void The_worksheet_has_four_sheets_named_in_the_language_of_the_person()
    {
        using var en = new XSSFWorkbook(new MemoryStream(TimesheetExcelExporter.Build(Document("en"))));
        using var bs = new XSSFWorkbook(new MemoryStream(TimesheetExcelExporter.Build(Document("bs"))));

        Assert.Equal(new[] { "Overview by days", "By company", "By ticket and task", "Raw list" }, Enumerable.Range(0, 4).Select(i => en.GetSheetName(i)));
        Assert.Equal(new[] { "Pregled po danima", "Po firmi", "Po tiketu i zadatku", "Sirovi popis" }, Enumerable.Range(0, 4).Select(i => bs.GetSheetName(i)));
    }

    [Fact]
    public void Every_sheet_says_who_made_it_when_for_whom_and_that_it_is_the_state_at_that_moment()
    {
        using var wb = new XSSFWorkbook(new MemoryStream(TimesheetExcelExporter.Build(Document("en"))));

        for (var i = 0; i < 4; i++)
        {
            var sheet = wb.GetSheetAt(i);
            Assert.Equal("Monthly work sheet", sheet.GetRow(0).GetCell(0).StringCellValue);
            Assert.Equal("Amra Hadžić", sheet.GetRow(1).GetCell(1).StringCellValue);
            Assert.Equal("03.2026", sheet.GetRow(2).GetCell(1).StringCellValue);
            Assert.Equal("Admin Admin", sheet.GetRow(3).GetCell(1).StringCellValue);
            Assert.Contains("04.03.2026 12:00 (Europe/Sarajevo)", sheet.GetRow(4).GetCell(1).StringCellValue); // UTC 11:00 is 12:00 there: the business zone, never UTC
            Assert.Contains("The state at the moment it was made", sheet.GetRow(5).GetCell(1).StringCellValue);
        }
    }

    [Fact]
    public void The_totals_of_the_sheets_agree_and_a_running_session_is_counted_up_to_the_moment_of_making()
    {
        using var wb = new XSSFWorkbook(new MemoryStream(TimesheetExcelExporter.Build(Document("en"))));
        // 5400 + 1800 + 1500 s and the running session from 09:00 to 11:00 on the 4th = 7200 s: 15900 s = 4.42 h (4.4166…), of which tasks 0.5 h
        var days = wb.GetSheetAt(0);
        var totalRow = days.GetRow(days.LastRowNum);
        Assert.Equal("Total", totalRow.GetCell(0).StringCellValue);
        Assert.Equal(3.92, totalRow.GetCell(1).NumericCellValue); // 14100 s of tickets
        Assert.Equal(0.5, totalRow.GetCell(2).NumericCellValue);
        Assert.Equal(4.42, totalRow.GetCell(3).NumericCellValue);

        var companies = wb.GetSheetAt(1);
        Assert.Equal("Alfa", companies.GetRow(8).GetCell(0).StringCellValue); // 5400 + 7200 s = 3.5 h
        Assert.Equal(3.5, companies.GetRow(8).GetCell(1).NumericCellValue);
        Assert.Equal("Tasks (no company)", companies.GetRow(companies.LastRowNum - 1).GetCell(0).StringCellValue);
        Assert.Equal(4.42, companies.GetRow(companies.LastRowNum).GetCell(1).NumericCellValue);

        var raw = wb.GetSheetAt(3);
        Assert.Equal("still running", raw.GetRow(11).GetCell(1).StringCellValue);
        Assert.Equal(4, raw.LastRowNum - 7); // four sessions
    }
}
