using TicketSystemTech.Application.Common;

namespace TicketSystemTech.Tests;

/// <summary>NOVO-SEC-10: a notice goes to a person at most once in a period.</summary>
public class NoticeThrottleTests
{
    private static readonly DateTime T0 = new(2031, 3, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void The_first_notice_goes_and_the_same_notice_within_the_period_does_not()
    {
        var throttle = new NoticeThrottle();
        var person = Guid.NewGuid();

        Assert.True(throttle.TryAcquire(person, T0, Day));
        Assert.False(throttle.TryAcquire(person, T0.AddMinutes(15), Day));
        Assert.False(throttle.TryAcquire(person, T0.AddHours(23), Day));
    }

    [Fact]
    public void The_notice_goes_again_once_the_period_has_passed_and_the_period_starts_from_the_one_that_went()
    {
        var throttle = new NoticeThrottle();
        var person = Guid.NewGuid();
        Assert.True(throttle.TryAcquire(person, T0, Day));

        Assert.True(throttle.TryAcquire(person, T0.AddHours(24), Day));
        Assert.False(throttle.TryAcquire(person, T0.AddHours(25), Day)); // counted from the second one now
    }

    [Fact]
    public void Each_person_has_a_period_of_their_own()
    {
        var throttle = new NoticeThrottle();

        Assert.True(throttle.TryAcquire(Guid.NewGuid(), T0, Day));
        Assert.True(throttle.TryAcquire(Guid.NewGuid(), T0, Day));
    }

    [Fact]
    public void Old_entries_are_dropped_so_that_it_does_not_grow_for_ever()
    {
        var throttle = new NoticeThrottle();
        var people = Enumerable.Range(0, 1200).Select(_ => Guid.NewGuid()).ToList();
        foreach (var person in people) throttle.TryAcquire(person, T0, Day);

        // a day and a bit later every old entry is expired, and asking about anybody works as if it were the first time
        Assert.True(throttle.TryAcquire(Guid.NewGuid(), T0.AddHours(30), Day));
        Assert.True(throttle.TryAcquire(people[0], T0.AddHours(30), Day));
    }
}
