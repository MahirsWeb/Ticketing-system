using TicketSystemTech.Application.WorkTime;

namespace TicketSystemTech.Tests;

/// <summary>T-33: what makes a logged stretch of work acceptable, on plain values (no database).</summary>
public class WorkSessionRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    private static WorkSessionRules.Span Span(int fromHour, int? toHour) =>
        new(Guid.NewGuid(), new DateTime(2026, 10, 19, fromHour, 0, 0, DateTimeKind.Utc),
            toHour.HasValue ? new DateTime(2026, 10, 19, toHour.Value, 0, 0, DateTimeKind.Utc) : null);

    private static DateTime At(int hour, int minute = 0) => new(2026, 10, 19, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void A_finished_session_with_no_neighbours_is_fine() =>
        Assert.Null(WorkSessionRules.Validate(At(9), At(10), Now, Array.Empty<WorkSessionRules.Span>()));

    [Fact]
    public void A_session_that_is_still_running_is_fine() =>
        Assert.Null(WorkSessionRules.Validate(At(9), null, Now, Array.Empty<WorkSessionRules.Span>()));

    [Fact]
    public void The_morning_and_the_afternoon_are_two_sessions_that_do_not_clash() =>
        Assert.Null(WorkSessionRules.Validate(At(9), At(10), Now, new[] { Span(7, 8), Span(10, 11) })); // touching is not overlapping

    [Theory]
    [InlineData(10, 10)] // ends when it starts
    [InlineData(10, 9)]  // ends before it starts
    public void A_session_must_end_after_it_starts(int start, int end) =>
        Assert.NotNull(WorkSessionRules.Validate(At(start), At(end), Now, Array.Empty<WorkSessionRules.Span>()));

    [Fact]
    public void A_session_cannot_start_in_the_future() =>
        Assert.Contains("future", WorkSessionRules.Validate(At(13), null, Now, Array.Empty<WorkSessionRules.Span>()));

    [Fact]
    public void A_few_minutes_of_clock_difference_between_browser_and_server_is_tolerated() =>
        Assert.Null(WorkSessionRules.Validate(At(12, 3), null, Now, Array.Empty<WorkSessionRules.Span>()));

    [Fact]
    public void A_session_cannot_end_in_the_future() =>
        Assert.Contains("future", WorkSessionRules.Validate(At(11), At(14), Now, Array.Empty<WorkSessionRules.Span>()));

    [Theory]
    [InlineData(8, 10, 9, 11)]  // starts inside the other
    [InlineData(9, 11, 8, 10)]  // ends inside the other
    [InlineData(8, 12, 9, 10)]  // contains the other
    [InlineData(9, 10, 8, 11)]  // is contained by the other
    [InlineData(9, 10, 9, 10)]  // identical
    public void Sessions_of_one_ticket_may_not_overlap(int otherFrom, int otherTo, int from, int to) =>
        Assert.Contains("overlaps", WorkSessionRules.Validate(At(from), At(to), Now, new[] { Span(otherFrom, otherTo) }));

    [Fact]
    public void While_one_session_is_running_no_later_one_can_be_added() =>
        Assert.Contains("still running", WorkSessionRules.Validate(At(11), At(11, 30), Now, new[] { Span(9, null) }));

    [Fact]
    public void A_session_before_the_running_one_is_fine_as_long_as_it_ends_first() =>
        Assert.Null(WorkSessionRules.Validate(At(7), At(9), Now, new[] { Span(9, null) }));

    [Fact]
    public void A_new_running_session_cannot_start_before_a_finished_one_that_ends_after_it() =>
        Assert.NotNull(WorkSessionRules.Validate(At(9), null, Now, new[] { Span(10, 11) }));

    [Fact]
    public void The_total_adds_up_finished_sessions_and_counts_a_running_one_up_to_now()
    {
        var total = WorkSessionRules.Total(new[] { Span(7, 8), Span(9, 10), Span(11, null) }, Now);

        Assert.Equal(TimeSpan.FromHours(3), total); // 1 h + 1 h + 1 h (11:00 to 12:00)
    }
}
