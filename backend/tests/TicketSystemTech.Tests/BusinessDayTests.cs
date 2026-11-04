using TicketSystemTech.Application.Sla;
using TicketSystemTech.Application.WorkTime;

namespace TicketSystemTech.Tests;

/// <summary>A "day" on the timeline is the business's local calendar day (TS-09.3), and what makes a session of a task acceptable (TS-33.1).</summary>
public class BusinessDayTests
{
    private static readonly TimeZoneInfo Sarajevo = BusinessTimeZone.Resolve("Europe/Sarajevo");
    private static readonly DateTime Noon = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_winter_day_starts_an_hour_before_midnight_UTC_and_a_summer_day_two_hours_before()
    {
        var winter = BusinessTimeZone.DayWindow(new DateTime(2026, 1, 16), Sarajevo, Noon);
        var summer = BusinessTimeZone.DayWindow(new DateTime(2026, 7, 16), Sarajevo, Noon);

        Assert.Equal(new DateTime(2026, 1, 15, 23, 0, 0, DateTimeKind.Utc), winter.StartUtc);
        Assert.Equal(new DateTime(2026, 1, 16, 23, 0, 0, DateTimeKind.Utc), winter.EndUtc);
        Assert.Equal(new DateTime(2026, 7, 15, 22, 0, 0, DateTimeKind.Utc), summer.StartUtc);
        Assert.Equal(new DateOnly(2026, 1, 16), winter.LocalDate);
    }

    [Fact]
    public void The_days_the_clocks_change_are_twenty_three_and_twenty_five_hours_long()
    {
        var spring = BusinessTimeZone.DayWindow(new DateTime(2026, 3, 29), Sarajevo, Noon);
        var autumn = BusinessTimeZone.DayWindow(new DateTime(2026, 10, 25), Sarajevo, Noon);

        Assert.Equal(TimeSpan.FromHours(23), spring.EndUtc - spring.StartUtc);
        Assert.Equal(TimeSpan.FromHours(25), autumn.EndUtc - autumn.StartUtc);
    }

    [Fact]
    public void Without_a_date_the_day_is_today_in_the_zone_even_when_it_is_still_yesterday_in_UTC()
    {
        // 23:30 UTC on 15 January is already 16 January in Sarajevo
        var window = BusinessTimeZone.DayWindow(null, Sarajevo, new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Utc));

        Assert.Equal(new DateOnly(2026, 1, 16), window.LocalDate);
    }

    [Fact]
    public void Only_the_calendar_date_of_what_was_typed_counts_not_its_time_or_kind()
    {
        var typed = BusinessTimeZone.DayWindow(new DateTime(2026, 1, 16, 18, 45, 0, DateTimeKind.Utc), Sarajevo, Noon);

        Assert.Equal(new DateOnly(2026, 1, 16), typed.LocalDate);
    }

    // ======================= the rules for a session of a task =======================

    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void A_session_longer_than_the_limit_is_refused_when_a_limit_is_given_and_accepted_when_it_is_not()
    {
        var start = Now.AddDays(-5);
        var end = Now.AddDays(-3);

        Assert.Contains("24 hours", WorkSessionRules.Validate(start, end, Now, Array.Empty<WorkSessionRules.Span>(), Day, "task"));
        Assert.Null(WorkSessionRules.Validate(start, end, Now, Array.Empty<WorkSessionRules.Span>())); // tickets have no such limit
        Assert.Null(WorkSessionRules.Validate(Now.AddHours(-24), Now, Now, Array.Empty<WorkSessionRules.Span>(), Day, "task")); // exactly the limit
    }

    [Fact]
    public void A_session_that_lasts_for_months_and_lies_in_the_future_is_refused_for_the_future_first()
    {
        var message = WorkSessionRules.Validate(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2030, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Now, Array.Empty<WorkSessionRules.Span>(), Day, "task");

        Assert.Contains("future", message);
    }

    [Fact]
    public void The_messages_name_what_the_session_belongs_to()
    {
        var other = new[] { new WorkSessionRules.Span(Guid.NewGuid(), Now.AddHours(-5), Now.AddHours(-3)) };

        Assert.Contains("same task", WorkSessionRules.Validate(Now.AddHours(-4), Now.AddHours(-2), Now, other, subject: "task"));
        Assert.Contains("same ticket", WorkSessionRules.Validate(Now.AddHours(-4), Now.AddHours(-2), Now, other));
    }
}
