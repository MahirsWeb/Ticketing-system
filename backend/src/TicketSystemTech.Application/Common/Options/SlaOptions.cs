namespace TicketSystemTech.Application.Common.Options;

public enum SlaClockMode
{
    /// <summary>The clock runs around the clock: a 24 h plan is due exactly 24 h after the ticket is opened.</summary>
    CalendarHours = 0,

    /// <summary>The clock only runs inside working hours on working days (and skips the configured holidays).</summary>
    BusinessHours = 1
}

public class SlaOptions
{
    public SlaClockMode Mode { get; set; } = SlaClockMode.CalendarHours;

    /// <summary>IANA id; the Windows equivalent is tried automatically on machines without ICU data.</summary>
    public string TimeZoneId { get; set; } = "Europe/Sarajevo";

    /// <summary>Local start and end of the working day, "HH:mm".</summary>
    public string WorkdayStart { get; set; } = "08:00";
    public string WorkdayEnd { get; set; } = "16:00";

    /// <summary>Working days. Deliberately null by default and NOT listed in appsettings.json: the configuration
    /// binder appends array items to an existing default instead of replacing it, so a configured ["Saturday"]
    /// would otherwise silently become Monday-Friday plus Saturday. Null means Monday to Friday.</summary>
    public DayOfWeek[]? WorkDays { get; set; }

    public static readonly DayOfWeek[] DefaultWorkDays =
        { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

    /// <summary>Non-working dates, "yyyy-MM-dd" (local calendar).</summary>
    public string[] Holidays { get; set; } = Array.Empty<string>();
}
