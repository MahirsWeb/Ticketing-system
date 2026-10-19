namespace TicketSystemTech.Application.Common;

/// <summary>How long the outbox waits before trying an e-mail again, and when it gives up.</summary>
public static class EmailRetryPolicy
{
    /// <summary>Eight tries over about two hours: long enough to ride out a provider outage, short enough that a mail about a
    /// ticket is not delivered a day late.</summary>
    public const int MaxAttempts = 8;

    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(30);

    /// <summary>The wait after the <paramref name="attemptsMade"/>-th failed attempt: 1, 2, 4, 8, 16 minutes, then 30.</summary>
    public static TimeSpan DelayAfter(int attemptsMade)
    {
        if (attemptsMade < 1) attemptsMade = 1;
        var minutes = Math.Pow(2, Math.Min(attemptsMade - 1, 10));
        var delay = TimeSpan.FromMinutes(minutes);
        return delay > Longest ? Longest : delay;
    }

    public static bool ShouldGiveUp(int attemptsMade) => attemptsMade >= MaxAttempts;
}
