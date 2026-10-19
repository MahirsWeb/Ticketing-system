using System.Globalization;
using TicketSystemTech.Application.Common.Interfaces;
using TicketSystemTech.Application.Common.Options;

namespace TicketSystemTech.Application.Sla;

/// <summary>
/// Before this existed an SLA plan's hours were only displayed: the due date was whatever staff typed into a date
/// box (or nothing), so compliance figures and overdue alerts rested on missing data.
/// </summary>
public class SlaCalculator : ISlaCalculator
{
    /// <summary>Guard against a misconfiguration (no working days, empty window) looping forever.</summary>
    private const int MaxDaysScanned = 3660;

    private readonly SlaOptions _options;
    private readonly Lazy<TimeZoneInfo> _timeZone;
    private readonly Lazy<(TimeSpan Start, TimeSpan End)> _window;
    private readonly Lazy<HashSet<DateOnly>> _holidays;

    public SlaCalculator(SlaOptions options)
    {
        _options = options;
        _timeZone = new Lazy<TimeZoneInfo>(() => ResolveTimeZone(_options.TimeZoneId));
        _window = new Lazy<(TimeSpan, TimeSpan)>(ParseWindow);
        _holidays = new Lazy<HashSet<DateOnly>>(() => _options.Holidays
            .Select(h => DateOnly.ParseExact(h.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToHashSet());
    }

    public DateTime ComputeDueDateUtc(DateTime startUtc, int resolutionHours)
    {
        var start = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        if (resolutionHours <= 0) return start;
        if (_options.Mode == SlaClockMode.CalendarHours) return start.AddHours(resolutionHours);

        var tz = _timeZone.Value;
        var (dayStart, dayEnd) = _window.Value;
        var workDays = (_options.WorkDays is { Length: > 0 } configured ? configured : SlaOptions.DefaultWorkDays).ToHashSet();
        var remaining = TimeSpan.FromHours(resolutionHours);

        var cursor = TimeZoneInfo.ConvertTimeFromUtc(start, tz); // wall-clock time in the business's own zone
        for (var scanned = 0; scanned < MaxDaysScanned; scanned++)
        {
            var date = cursor.Date;
            var isWorkingDay = workDays.Contains(date.DayOfWeek) && !_holidays.Value.Contains(DateOnly.FromDateTime(date));
            if (isWorkingDay)
            {
                var from = cursor > date + dayStart ? cursor : date + dayStart;
                var until = date + dayEnd;
                if (from < until)
                {
                    var available = until - from;
                    if (remaining <= available) return ToUtc(from + remaining, tz);
                    remaining -= available;
                }
            }

            cursor = date.AddDays(1);
        }

        throw new InvalidOperationException(
            "SLA business calendar has no working time: check Sla:WorkDays, Sla:WorkdayStart/WorkdayEnd and Sla:Holidays.");
    }

    private static DateTime ToUtc(DateTime wallClock, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1); // fell into a spring-forward gap
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, tz);
    }

    private (TimeSpan Start, TimeSpan End) ParseWindow()
    {
        var start = TimeSpan.ParseExact(_options.WorkdayStart, @"hh\:mm", CultureInfo.InvariantCulture);
        var end = TimeSpan.ParseExact(_options.WorkdayEnd, @"hh\:mm", CultureInfo.InvariantCulture);
        if (end <= start) throw new InvalidOperationException("Sla:WorkdayEnd must be later than Sla:WorkdayStart.");
        return (start, end);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        foreach (var candidate in new[] { id, "Central European Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        throw new InvalidOperationException($"Sla:TimeZoneId '{id}' is not a known time zone on this machine.");
    }
}
