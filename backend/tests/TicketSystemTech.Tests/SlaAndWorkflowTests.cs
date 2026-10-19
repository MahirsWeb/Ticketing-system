using Microsoft.Extensions.Configuration;
using TicketSystemTech.Application.Common.Options;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Domain.Enums;
using TicketSystemTech.Domain.Workflow;

namespace TicketSystemTech.Tests;

/// <summary>Regression tests for T-08: an SLA plan's hours were only displayed, never used to set a due date.</summary>
public class SlaAndWorkflowTests
{
    private static readonly TimeZoneInfo Sarajevo = FindSarajevo();

    private static TimeZoneInfo FindSarajevo()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Sarajevo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"); }
    }

    private static DateTime LocalToUtc(int year, int month, int day, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), Sarajevo);

    private static SlaCalculator Business(params string[] holidays) =>
        new(new SlaOptions { Mode = SlaClockMode.BusinessHours, Holidays = holidays });

    // ---- calendar hours ----

    [Fact]
    public void Calendar_mode_is_plain_elapsed_time()
    {
        var start = new DateTime(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc); // a Friday afternoon

        var due = new SlaCalculator(new SlaOptions()).ComputeDueDateUtc(start, 24);

        Assert.Equal(start.AddHours(24), due);
        Assert.Equal(DateTimeKind.Utc, due.Kind);
    }

    [Fact]
    public void A_plan_without_hours_is_due_immediately()
    {
        var start = new DateTime(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal(start, new SlaCalculator(new SlaOptions()).ComputeDueDateUtc(start, 0));
    }

    // ---- business hours (Mon-Fri 08:00-16:00 Europe/Sarajevo) ----

    [Fact]
    public void Business_time_inside_one_working_day()
    {
        var due = Business().ComputeDueDateUtc(LocalToUtc(2026, 10, 14, 9), 4); // Wednesday 09:00 + 4 h

        Assert.Equal(LocalToUtc(2026, 10, 14, 13), due);
    }

    [Fact]
    public void Business_time_rolls_over_the_weekend()
    {
        var due = Business().ComputeDueDateUtc(LocalToUtc(2026, 10, 16, 15), 2); // Friday 15:00 + 2 h: 1 h Friday, 1 h Monday

        Assert.Equal(LocalToUtc(2026, 10, 19, 9), due);
    }

    [Fact]
    public void A_ticket_opened_at_night_starts_counting_the_next_morning()
    {
        var due = Business().ComputeDueDateUtc(LocalToUtc(2026, 10, 14, 22), 1);

        Assert.Equal(LocalToUtc(2026, 10, 15, 9), due);
    }

    [Fact]
    public void A_ticket_opened_on_a_saturday_starts_counting_on_monday()
    {
        var due = Business().ComputeDueDateUtc(LocalToUtc(2026, 10, 17, 11), 3);

        Assert.Equal(LocalToUtc(2026, 10, 19, 11), due);
    }

    [Fact]
    public void Holidays_do_not_count()
    {
        // Thursday 15 October is a holiday: Wednesday 15:00 + 2 h = 1 h Wednesday + 1 h Friday.
        var due = Business("2026-10-15").ComputeDueDateUtc(LocalToUtc(2026, 10, 14, 15), 2);

        Assert.Equal(LocalToUtc(2026, 10, 16, 9), due);
    }

    [Fact]
    public void A_full_working_day_plan_spans_into_the_next_day()
    {
        var due = Business().ComputeDueDateUtc(LocalToUtc(2026, 10, 14, 8), 16); // two full 8 h days

        Assert.Equal(LocalToUtc(2026, 10, 15, 16), due);
    }

    [Fact]
    public void The_result_follows_local_clock_time_across_the_daylight_saving_change()
    {
        // Clocks go back on Sunday 25 October 2026: 09:00 local is 07:00 UTC before, 08:00 UTC after.
        var due = Business().ComputeDueDateUtc(LocalToUtc(2026, 10, 23, 15), 2); // Friday 15:00 + 2 h -> Monday 09:00 local

        Assert.Equal(LocalToUtc(2026, 10, 26, 9), due);
        Assert.Equal(8, due.Hour);
    }

    [Fact]
    public void Configured_working_days_replace_the_default_instead_of_adding_to_it()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mode"] = "BusinessHours", ["WorkDays:0"] = "Saturday" })
            .Build();
        var options = config.Get<SlaOptions>()!;

        // Only Saturday works: Friday 15:00 + 2 h lands on Saturday 10:00, not on Friday 17:00-ish or Monday.
        var due = new SlaCalculator(options).ComputeDueDateUtc(LocalToUtc(2026, 10, 16, 15), 2);

        Assert.Equal(new[] { DayOfWeek.Saturday }, options.WorkDays);
        Assert.Equal(LocalToUtc(2026, 10, 17, 10), due);
    }

    [Fact]
    public void A_calendar_without_working_time_fails_loudly_instead_of_looping()
    {
        // Every day is a holiday for the whole scan range: the guard must stop the loop, not hang.
        var days = Enumerable.Range(0, 3700).Select(i => new DateTime(2026, 1, 1).AddDays(i).ToString("yyyy-MM-dd")).ToArray();
        var calc = new SlaCalculator(new SlaOptions { Mode = SlaClockMode.BusinessHours, Holidays = days });

        Assert.Throws<InvalidOperationException>(() => calc.ComputeDueDateUtc(new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc), 1));
    }

    [Fact]
    public void An_inverted_working_window_is_rejected()
    {
        var calc = new SlaCalculator(new SlaOptions { Mode = SlaClockMode.BusinessHours, WorkdayStart = "16:00", WorkdayEnd = "08:00" });

        Assert.Throws<InvalidOperationException>(() => calc.ComputeDueDateUtc(DateTime.UtcNow, 1));
    }

    [Fact]
    public void The_shipped_appsettings_section_binds_to_the_options()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sla:Mode"] = "BusinessHours", ["Sla:Holidays:0"] = "2026-12-25" })
            .Build();

        var options = config.GetSection("Sla").Get<SlaOptions>()!;

        Assert.Equal(SlaClockMode.BusinessHours, options.Mode);
        Assert.Equal("Europe/Sarajevo", options.TimeZoneId);
        Assert.Null(options.WorkDays); // defaults to Monday-Friday in the calculator
        Assert.Equal(new[] { "2026-12-25" }, options.Holidays);
        // and the bound options produce a due date: Thursday 24 Dec 15:00 local + 2 h skips the 25th (holiday) and the weekend.
        var due = new SlaCalculator(options).ComputeDueDateUtc(LocalToUtc(2026, 12, 24, 15), 2);
        Assert.Equal(LocalToUtc(2026, 12, 28, 9), due);
    }

    // ---- workflow ----

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Open)]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Open, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Open, TicketStatus.Closed)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Open)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Open)]
    [InlineData(TicketStatus.Closed, TicketStatus.Open)]
    public void Legal_transitions(TicketStatus from, TicketStatus to) => Assert.True(TicketWorkflow.CanTransition(from, to));

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Closed)]
    [InlineData(TicketStatus.New, TicketStatus.InProgress)]
    [InlineData(TicketStatus.New, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Open, TicketStatus.New)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress)]
    [InlineData(TicketStatus.Closed, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed, TicketStatus.Closed)]
    [InlineData(TicketStatus.Open, TicketStatus.Open)]
    public void Illegal_transitions(TicketStatus from, TicketStatus to) => Assert.False(TicketWorkflow.CanTransition(from, to));

    [Theory]
    [InlineData(TicketStatus.Open, true)]
    [InlineData(TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.Resolved, true)]
    [InlineData(TicketStatus.New, false)]
    [InlineData(TicketStatus.Closed, false)]
    public void Only_live_statuses_can_be_set_directly(TicketStatus status, bool expected) =>
        Assert.Equal(expected, TicketWorkflow.IsDirectlyAssignable(status));
}
