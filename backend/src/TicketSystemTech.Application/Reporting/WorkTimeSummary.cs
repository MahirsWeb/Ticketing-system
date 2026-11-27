using TicketSystemTech.Application.Sla;

namespace TicketSystemTech.Application.Reporting;

/// <summary>
/// One recorded stretch of work: a work session on a ticket or on a task. Only what a person recorded (a session of <c>TicketWorkSession</c> or
/// <c>WorkTaskSession</c>); the stand-in blocks that the Gantt chart draws for tickets without recorded time are NOT work entries.
/// </summary>
public sealed record WorkEntry(
    Guid SessionId,
    Guid UserId,
    bool IsTask,
    Guid ItemId,
    string ItemNumber, // the ticket number; empty for a task
    string ItemTitle,
    Guid? CompanyId, // null for a task: tasks have no company
    string? CompanyName,
    DateTime StartUtc,
    DateTime? EndUtc // null: still running
);

/// <summary>A day. <see cref="RunningCount"/> is how many of its sessions are still running (they are counted up to the moment the figure is made, so the day will have more later).</summary>
public sealed record DayWork(DateOnly Day, long TicketSeconds, long TaskSeconds, int RunningCount = 0)
{
    public long TotalSeconds => TicketSeconds + TaskSeconds;
}

public sealed record WeekWork(DateOnly WeekStart, long TicketSeconds, long TaskSeconds, int RunningCount = 0)
{
    public long TotalSeconds => TicketSeconds + TaskSeconds;
}

public sealed record GroupWork(string Key, string Label, bool IsTask, long Seconds, int RunningCount = 0);

/// <summary>
/// Adds up recorded work. Three rules, the same everywhere (the screens and the worksheet):
/// a session still running counts up to the moment the figure is made; seconds are added up and rounded to hours only at the very end (never
/// session by session, which would drift); and a session belongs to the day, the week and the month it STARTED in, in the business time zone.
/// </summary>
public static class WorkTimeSummary
{
    /// <summary>The seconds of one session; a session that ends before it starts (a wrong correction) is nothing, not negative.</summary>
    public static long Seconds(WorkEntry entry, DateTime asOfUtc)
    {
        var end = entry.EndUtc ?? asOfUtc;
        return end > entry.StartUtc ? (long)Math.Floor((end - entry.StartUtc).TotalSeconds) : 0;
    }

    /// <summary>The decimals of an hour that are shown, on the screens' server side and in the workbooks (their number format is made from it). One place to change.</summary>
    public const int HoursDecimals = 2;

    /// <summary>The number format of an hours cell in a workbook, made from <see cref="HoursDecimals"/>.</summary>
    public static string HoursNumberFormat => HoursDecimals > 0 ? "0." + new string('0', HoursDecimals) : "0";

    /// <summary>Seconds as hours, rounded once, on a sum.</summary>
    public static double Hours(long seconds) => Math.Round(seconds / 3600.0, HoursDecimals, MidpointRounding.AwayFromZero);

    /// <summary>How many of the sessions are still running.</summary>
    public static int RunningCount(IEnumerable<WorkEntry> entries) => entries.Count(e => e.EndUtc is null);

    public static long Total(IEnumerable<WorkEntry> entries, DateTime asOfUtc) => entries.Sum(e => Seconds(e, asOfUtc));

    /// <summary>A line for every day of the range, also the days with no work, oldest first.</summary>
    public static List<DayWork> ByDay(IEnumerable<WorkEntry> entries, DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, DateTime asOfUtc)
    {
        var perDay = entries
            .GroupBy(e => BusinessTimeZone.LocalDate(e.StartUtc, zone))
            .ToDictionary(g => g.Key, g => (Ticket: g.Where(e => !e.IsTask).Sum(e => Seconds(e, asOfUtc)), Task: g.Where(e => e.IsTask).Sum(e => Seconds(e, asOfUtc)), Running: g.Count(e => e.EndUtc is null)));

        var days = new List<DayWork>();
        for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
        {
            var (ticket, task, running) = perDay.GetValueOrDefault(d);
            days.Add(new DayWork(d, ticket, task, running));
        }
        return days;
    }

    /// <summary>The same by week (Monday to Sunday), only the weeks with a day in the range.</summary>
    public static List<WeekWork> ByWeek(IEnumerable<DayWork> days) =>
        days.GroupBy(d => BusinessMonth.WeekStart(d.Day))
            .OrderBy(g => g.Key)
            .Select(g => new WeekWork(g.Key, g.Sum(d => d.TicketSeconds), g.Sum(d => d.TaskSeconds), g.Sum(d => d.RunningCount)))
            .ToList();

    /// <summary>By company; the work on tasks, which have no company, is its own group (labelled by the caller, in the language of the document).</summary>
    public static List<GroupWork> ByCompany(IEnumerable<WorkEntry> entries, string tasksLabel, DateTime asOfUtc) =>
        entries
            .GroupBy(e => e.IsTask ? "task" : (e.CompanyId?.ToString() ?? "none"))
            .Select(g => new GroupWork(g.Key, g.Key == "task" ? tasksLabel : g.First().CompanyName ?? "", g.Key == "task", g.Sum(e => Seconds(e, asOfUtc)), g.Count(e => e.EndUtc is null)))
            .OrderBy(g => g.IsTask) // the tasks last
            .ThenByDescending(g => g.Seconds)
            .ThenBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>By ticket or task.</summary>
    public static List<GroupWork> ByItem(IEnumerable<WorkEntry> entries, DateTime asOfUtc) =>
        entries
            .GroupBy(e => (e.IsTask, e.ItemId))
            .Select(g => new GroupWork(g.Key.ItemId.ToString(), g.First().IsTask ? g.First().ItemTitle : $"{g.First().ItemNumber} {g.First().ItemTitle}".Trim(), g.Key.IsTask, g.Sum(e => Seconds(e, asOfUtc)), g.Count(e => e.EndUtc is null)))
            .OrderByDescending(g => g.Seconds)
            .ThenBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>The working days (Monday to Friday, no calendar of holidays) of the range with no recorded second, from <paramref name="notBefore"/> (the day the account was made) to <paramref name="notAfter"/> (today).</summary>
    public static List<DateOnly> DaysWithoutWork(IEnumerable<DayWork> days, DateOnly notBefore, DateOnly notAfter) =>
        days.Where(d => d.Day >= notBefore && d.Day <= notAfter && BusinessMonth.IsWorkingDay(d.Day) && d.TotalSeconds == 0).Select(d => d.Day).ToList();
}
