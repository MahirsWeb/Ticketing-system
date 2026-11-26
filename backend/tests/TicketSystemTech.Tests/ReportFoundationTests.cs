using TicketSystemTech.Application.Reporting;
using TicketSystemTech.Application.Sla;
using TicketSystemTech.Application.Tickets;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Tests;

/// <summary>The building blocks of the reports: the business month, the percentile, the reading of a transfer and the time a ticket spent in each status.</summary>
public class ReportFoundationTests
{
    private static readonly TimeZoneInfo Zone = BusinessTimeZone.Resolve("Europe/Sarajevo");
    private static readonly Guid A = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // ---- the business month ----

    [Fact]
    public void A_month_starts_and_ends_at_local_midnight_not_at_UTC_midnight()
    {
        var (start, end) = BusinessMonth.Window(2026, 1, Zone); // winter: UTC+1
        Assert.Equal(new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void A_month_that_holds_the_change_of_the_clocks_is_a_hour_shorter_than_its_days_say()
    {
        var (start, end) = BusinessMonth.Window(2026, 3, Zone); // the clocks went forward on 29 March
        Assert.Equal(new DateTime(2026, 2, 28, 23, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 3, 31, 22, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(TimeSpan.FromDays(31) - TimeSpan.FromHours(1), end - start);
    }

    [Fact]
    public void An_instant_at_the_turn_of_the_month_belongs_to_the_local_month()
    {
        Assert.Equal((2026, 1), BusinessMonth.Of(new DateTime(2026, 1, 31, 22, 59, 59, DateTimeKind.Utc), Zone));
        Assert.Equal((2026, 2), BusinessMonth.Of(new DateTime(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc), Zone)); // 00:00 on 1 February in Sarajevo, still 31 January in UTC
    }

    [Fact]
    public void Weeks_run_from_Monday_and_the_weekend_is_not_a_working_day()
    {
        Assert.Equal(new DateOnly(2026, 11, 23), BusinessMonth.WeekStart(new DateOnly(2026, 11, 29))); // a Sunday belongs to the week before it
        Assert.Equal(new DateOnly(2026, 11, 23), BusinessMonth.WeekStart(new DateOnly(2026, 11, 23)));
        Assert.True(BusinessMonth.IsWorkingDay(new DateOnly(2026, 11, 27)));
        Assert.False(BusinessMonth.IsWorkingDay(new DateOnly(2026, 11, 28)));
        Assert.Equal(28, BusinessMonth.Days(2026, 2).Count());
    }

    // ---- the percentile ----

    [Fact]
    public void The_percentile_is_taken_by_linear_interpolation()
    {
        double[] four = { 1, 2, 3, 4 };
        Assert.Equal(2.5, Distribution.Percentile(four, 0.5), 9);
        Assert.Equal(3.7, Distribution.Percentile(four, 0.9), 9);
        Assert.Equal(1, Distribution.Percentile(four, 0), 9);
        Assert.Equal(4, Distribution.Percentile(four, 1), 9);
    }

    [Fact]
    public void The_median_and_the_percentile_show_what_the_average_hides()
    {
        var d = Distribution.Of(new double[] { 100, 1, 1, 1, 1 }); // unordered on purpose
        Assert.Equal(5, d.Count);
        Assert.Equal(20.8, d.Average, 9);
        Assert.Equal(1, d.Median, 9);
        Assert.Equal(60.4, d.P90, 9);
    }

    [Fact]
    public void One_value_and_no_value_have_a_percentile_too()
    {
        Assert.Equal(7, Distribution.Of(new double[] { 7 }).P90);
        Assert.Equal(Distribution.Empty, Distribution.Of(Array.Empty<double>()));
    }

    // ---- the transfer ----

    [Fact]
    public void A_transfer_is_written_and_read_as_two_departments()
    {
        Assert.Equal($"Branch {A} -> {B}", TicketAuditDetails.Transferred(A, B)); // the words that are already in the database
        Assert.Equal("Branch - -> " + B, TicketAuditDetails.Transferred(null, B));
        Assert.Equal((A, B), TicketAuditDetails.Transfer(TicketAuditDetails.Transferred(A, B)));
        Assert.Equal((null, B), TicketAuditDetails.Transfer(TicketAuditDetails.Transferred(null, B)));
        Assert.Equal((A, B), TicketAuditDetails.Transfer($"Branch {A} -> {B}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Branch")]
    [InlineData("Branch a -> b")] // the test data of the feed of events: not ids
    [InlineData("Branch - -> -")]
    [InlineData("Branch aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa ->")]
    [InlineData("branch aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa -> bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
    [InlineData("Open -> Closed")]
    public void A_transfer_that_is_not_understood_is_no_transfer_and_not_an_error(string? text) => Assert.Null(TicketAuditDetails.Transfer(text));

    // ---- the time in each status ----

    private static readonly DateTime T0 = new(2026, 5, 4, 8, 0, 0, DateTimeKind.Utc);

    private static TrailEvent Row(double hours, string action, string? details = null) => new(T0.AddHours(hours), action, details);

    private static TrailEvent Status(double hours, TicketStatus from, TicketStatus to) => Row(hours, "TicketStatusChanged", TicketAuditDetails.StatusChanged(from, to));

    private static double Hours(StatusTimeline timeline, TicketStatus status, double asOfHours) =>
        timeline.Totals(T0.AddHours(asOfHours)).GetValueOrDefault(status).TotalHours;

    [Fact]
    public void A_ticket_that_a_client_made_and_that_went_the_whole_way_has_every_stretch_known()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, T0.AddHours(2), T0.AddHours(6), TicketStatus.Closed, new[]
        {
            Row(2, "TicketOpened", TicketAuditDetails.Opened(A, B)),
            Status(3, TicketStatus.Open, TicketStatus.InProgress),
            Status(5, TicketStatus.InProgress, TicketStatus.Resolved),
            Row(6, "TicketClosed", "Ticket #1")
        });

        Assert.Equal(2, Hours(timeline, TicketStatus.New, 10));
        Assert.Equal(1, Hours(timeline, TicketStatus.Open, 10));
        Assert.Equal(2, Hours(timeline, TicketStatus.InProgress, 10));
        Assert.Equal(1, Hours(timeline, TicketStatus.Resolved, 10));
        Assert.Equal(4, Hours(timeline, TicketStatus.Closed, 10)); // the stretch that goes on counts to the moment asked about
        Assert.Equal(TimeSpan.Zero, timeline.Unknown(T0.AddHours(10)));
        Assert.Null(timeline.Spans[^1].ToUtc);
    }

    [Fact]
    public void A_ticket_made_open_on_behalf_of_a_client_needs_no_opened_row()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, T0, null, TicketStatus.Open, Array.Empty<TrailEvent>());

        Assert.Equal(5, Hours(timeline, TicketStatus.Open, 5));
        Assert.Equal(TimeSpan.Zero, timeline.Unknown(T0.AddHours(5)));
    }

    [Fact]
    public void The_closing_is_taken_from_the_ticket_when_the_trail_does_not_have_it()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, T0, T0.AddHours(4), TicketStatus.Closed, Array.Empty<TrailEvent>());

        Assert.Equal(4, Hours(timeline, TicketStatus.Open, 9));
        Assert.Equal(5, Hours(timeline, TicketStatus.Closed, 9));
    }

    [Fact]
    public void A_closing_that_is_in_the_trail_and_on_the_ticket_is_counted_once()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, T0, T0.AddHours(4).AddSeconds(1), TicketStatus.Closed, new[] { Row(4, "TicketClosed") });

        Assert.Equal(2, timeline.Spans.Count);
        Assert.Equal(TimeSpan.Zero, timeline.Unknown(T0.AddHours(9)));
    }

    [Fact]
    public void A_reopened_ticket_has_two_stretches_open_and_one_closed()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, T0, null, TicketStatus.Open, new[]
        {
            Row(0, "TicketOpened"), Row(6, "TicketClosed"), Row(8, "TicketReopened")
        });

        Assert.Equal(6 + 3, Hours(timeline, TicketStatus.Open, 11));
        Assert.Equal(2, Hours(timeline, TicketStatus.Closed, 11));
    }

    [Fact]
    public void A_ticket_with_no_trail_and_not_new_is_unknown_all_the_way_and_never_guessed()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, null, null, TicketStatus.InProgress, Array.Empty<TrailEvent>());

        Assert.Empty(timeline.Totals(T0.AddHours(5)));
        Assert.Equal(TimeSpan.FromHours(5), timeline.Unknown(T0.AddHours(5)));
    }

    [Fact]
    public void A_ticket_that_nothing_happened_to_and_is_new_has_been_new_all_the_time()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, null, null, TicketStatus.New, Array.Empty<TrailEvent>());

        Assert.Equal(5, Hours(timeline, TicketStatus.New, 5));
    }

    [Fact]
    public void A_row_that_contradicts_the_rows_before_it_makes_the_stretch_unknown()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, T0, null, TicketStatus.Resolved, new[]
        {
            Status(2, TicketStatus.Open, TicketStatus.InProgress),
            Status(5, TicketStatus.Open, TicketStatus.Resolved) // says Open, but the ticket was InProgress
        });

        Assert.Equal(2, Hours(timeline, TicketStatus.Open, 9));
        Assert.Equal(0, Hours(timeline, TicketStatus.InProgress, 9));
        Assert.Equal(3, timeline.Unknown(T0.AddHours(9)).TotalHours);
        Assert.Equal(4, Hours(timeline, TicketStatus.Resolved, 9));
    }

    [Fact]
    public void A_trail_that_ends_in_another_status_than_the_ticket_has_leaves_its_end_unknown()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, T0, null, TicketStatus.Resolved, new[] { Status(2, TicketStatus.Open, TicketStatus.InProgress) });

        Assert.Equal(2, Hours(timeline, TicketStatus.Open, 9));
        Assert.Equal(7, timeline.Unknown(T0.AddHours(9)).TotalHours); // the row that moved it on is missing
    }

    [Fact]
    public void A_status_row_that_is_not_understood_makes_what_follows_unknown()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, T0, null, TicketStatus.Open, new[] { Row(3, "TicketStatusChanged", "gibberish") });

        Assert.Equal(3, Hours(timeline, TicketStatus.Open, 9));
        Assert.Equal(6, timeline.Unknown(T0.AddHours(9)).TotalHours);
    }

    [Fact]
    public void Rows_of_other_kinds_and_rows_from_before_the_ticket_do_not_disturb_it()
    {
        var timeline = TicketStatusTimelineBuilder.Build(T0, T0, null, TicketStatus.Open, new[]
        {
            Row(-1, "TicketOpened"), // clock skew: before the ticket existed
            Row(2, "TicketTransferred", TicketAuditDetails.Transferred(A, B)),
            Row(3, "TicketAssigneeAdded", TicketAuditDetails.Added(A))
        });

        Assert.Equal(6, Hours(timeline, TicketStatus.Open, 6));
        Assert.Equal(TimeSpan.Zero, timeline.Unknown(T0.AddHours(6)));
    }
}
