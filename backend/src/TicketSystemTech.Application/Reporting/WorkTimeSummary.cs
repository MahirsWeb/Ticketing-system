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

public sealed record DayWork(DateOnly Day, long TicketSeconds, long TaskSeconds)
{
    public long TotalSeconds => TicketSeconds + TaskSeconds;
}

public sealed record WeekWork(DateOnly WeekStart, long TicketSeconds, long TaskSeconds)
{
    public long TotalSeconds => TicketSeconds + TaskSeconds;
}

public sealed record GroupWork(string Key, string Label, bool IsTask, long Seconds);

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

    /// <summary>Seconds as hours, to two places. Done once, on a sum.</summary>
    public static double Hours(long seconds) => Math.Round(seconds / 3600.0, 2, MidpointRounding.AwayFromZero);

    public static long Total(IEnumerable<WorkEntry> entries, DateTime asOfUtc) => entries.Sum(e => Seconds(e, asOfUtc));

    /// <summary>A line for every day of the range, also the days with no work, oldest first.</summary>
    public static List<DayWork> ByDay(IEnumerable<WorkEntry> entries, DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, DateTime asOfUtc)
    {
        var perDay = entries
            .GroupBy(e => BusinessTimeZone.LocalDate(e.StartUtc, zone))
            .ToDictionary(g => g.Key, g => (Ticket: g.Where(e => !e.IsTask).Sum(e => Seconds(e, asOfUtc)), Task: g.Where(e => e.IsTask).Sum(e => Seconds(e, asOfUtc))));

        var days = new List<DayWork>();
        for (var d = firstDay; d <= lastDay; d = d.AddDays(1))
        {
            var (ticket, task) = perDay.GetValueOrDefault(d);
            days.Add(new DayWork(d, ticket, task));
        }
        return days;
    }

    /// <summary>The same by week (Monday to Sunday), only the weeks with a day in the range.</summary>
    public static List<WeekWork> ByWeek(IEnumerable<DayWork> days) =>
        days.GroupBy(d => BusinessMonth.WeekStart(d.Day))
            .OrderBy(g => g.Key)
            .Select(g => new WeekWork(g.Key, g.Sum(d => d.TicketSeconds), g.Sum(d => d.TaskSeconds)))
            .ToList();

    /// <summary>By company; the work on tasks, which have no company, is its own group (labelled by the caller, in the language of the document).</summary>
    public static List<GroupWork> ByCompany(IEnumerable<WorkEntry> entries, string tasksLabel, DateTime asOfUtc) =>
        entries
            .GroupBy(e => e.IsTask ? "task" : (e.CompanyId?.ToString() ?? "none"))
            .Select(g => new GroupWork(g.Key, g.Key == "task" ? tasksLabel : g.First().CompanyName ?? "", g.Key == "task", g.Sum(e => Seconds(e, asOfUtc))))
            .OrderBy(g => g.IsTask) // the tasks last
            .ThenByDescending(g => g.Seconds)
            .ThenBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>By ticket or task.</summary>
    public static List<GroupWork> ByItem(IEnumerable<WorkEntry> entries, DateTime asOfUtc) =>
        entries
            .GroupBy(e => (e.IsTask, e.ItemId))
            .Select(g => new GroupWork(g.Key.ItemId.ToString(), g.First().IsTask ? g.First().ItemTitle : $"{g.First().ItemNumber} {g.First().ItemTitle}".Trim(), g.Key.IsTask, g.Sum(e => Seconds(e, asOfUtc))))
            .OrderByDescending(g => g.Seconds)
            .ThenBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>The working days (Monday to Friday, no calendar of holidays) of the range with no recorded second, from <paramref name="notBefore"/> (the day the account was made) to <paramref name="notAfter"/> (today).</summary>
    public static List<DateOnly> DaysWithoutWork(IEnumerable<DayWork> days, DateOnly notBefore, DateOnly notAfter) =>
        days.Where(d => d.Day >= notBefore && d.Day <= notAfter && BusinessMonth.IsWorkingDay(d.Day) && d.TotalSeconds == 0).Select(d => d.Day).ToList();
}
