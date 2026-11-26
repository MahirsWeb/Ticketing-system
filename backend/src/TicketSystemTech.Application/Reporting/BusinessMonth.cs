using TicketSystemTech.Application.Sla;

namespace TicketSystemTech.Application.Reporting;

/// <summary>
/// A calendar month and the days and weeks in it, in the business time zone (never UTC): a session that starts at 00:30 local time on the
/// 1st is in that month although it is still the 31st in UTC.
/// </summary>
public static class BusinessMonth
{
    /// <summary>The UTC instants the month starts at (inclusive) and ends at (exclusive). Month 1–12.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) Window(int year, int month, TimeZoneInfo zone)
    {
        var first = new DateOnly(year, month, 1);
        // The "now" of DayWindow is only for a call without a date; here there is always one, so no clock is read.
        var start = BusinessTimeZone.DayWindow(first.ToDateTime(TimeOnly.MinValue), zone, DateTime.UnixEpoch).StartUtc;
        var end = BusinessTimeZone.DayWindow(first.AddMonths(1).ToDateTime(TimeOnly.MinValue), zone, DateTime.UnixEpoch).StartUtc;
        return (start, end);
    }

    /// <summary>The local month an instant is in.</summary>
    public static (int Year, int Month) Of(DateTime utc, TimeZoneInfo zone)
    {
        var day = BusinessTimeZone.LocalDate(utc, zone);
        return (day.Year, day.Month);
    }

    public static IEnumerable<DateOnly> Days(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        for (var d = first; d.Month == month; d = d.AddDays(1)) yield return d;
    }

    /// <summary>The Monday of the week a day is in (weeks run Monday to Sunday).</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Monday to Friday. There is no calendar of holidays.</summary>
    public static bool IsWorkingDay(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
