using TicketSystemTech.Application.Common;

namespace TicketSystemTech.Tests;

public class EmailRetryPolicyTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(7, 30)]
    [InlineData(50, 30)]
    public void The_wait_doubles_with_every_failed_attempt_and_stops_at_half_an_hour(int attemptsMade, int expectedMinutes) =>
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), EmailRetryPolicy.DelayAfter(attemptsMade));

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_nonsense_attempt_count_still_gives_the_shortest_wait(int attemptsMade) =>
        Assert.Equal(TimeSpan.FromMinutes(1), EmailRetryPolicy.DelayAfter(attemptsMade));

    [Fact]
    public void The_message_is_given_up_exactly_when_the_attempts_are_used_up()
    {
        Assert.False(EmailRetryPolicy.ShouldGiveUp(EmailRetryPolicy.MaxAttempts - 1));
        Assert.True(EmailRetryPolicy.ShouldGiveUp(EmailRetryPolicy.MaxAttempts));
        Assert.True(EmailRetryPolicy.ShouldGiveUp(EmailRetryPolicy.MaxAttempts + 1));
    }

    [Fact]
    public void All_attempts_together_cover_a_provider_outage_of_a_couple_of_hours()
    {
        // Seven waits sit between eight attempts: 1+2+4+8+16+30+30 minutes.
        var total = Enumerable.Range(1, EmailRetryPolicy.MaxAttempts - 1).Select(EmailRetryPolicy.DelayAfter).Aggregate(TimeSpan.Zero, (a, b) => a + b);

        Assert.InRange(total.TotalMinutes, 60, 240);
    }
}
