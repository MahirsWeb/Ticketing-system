namespace TicketSystemTech.Application.WorkTime;

/// <summary>What makes a logged work session acceptable. Kept free of the database so it can be tested on plain values.</summary>
public static class WorkSessionRules
{
    /// <summary>Browsers and servers disagree about the time by a little; a session that "starts" a few minutes ahead is not an error.</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    /// <summary>A session already stored on the same ticket (<paramref name="EndedAtUtc"/> null = still running).</summary>
    public sealed record Span(Guid Id, DateTime StartedAtUtc, DateTime? EndedAtUtc);

    /// <summary>
    /// Returns why the session cannot be saved, or null if it can. <paramref name="others"/> are the ticket's other sessions
    /// (leave out the one being edited). Sessions of one ticket may not overlap: one person works a ticket at a time, so an
    /// overlap would be counted twice. That also means a ticket has at most one running session.
    /// </summary>
    public static string? Validate(DateTime startedAtUtc, DateTime? endedAtUtc, DateTime nowUtc, IEnumerable<Span> others)
    {
        if (endedAtUtc is { } end && end <= startedAtUtc)
            return "The end of a work session must be after its start.";
        if (startedAtUtc > nowUtc + ClockTolerance)
            return "A work session cannot start in the future.";
        if (endedAtUtc is { } endsAt && endsAt > nowUtc + ClockTolerance)
            return "A work session cannot end in the future.";

        var newEnd = endedAtUtc ?? DateTime.MaxValue;
        foreach (var other in others)
        {
            var otherEnd = other.EndedAtUtc ?? DateTime.MaxValue;
            if (startedAtUtc < otherEnd && other.StartedAtUtc < newEnd)
                return other.EndedAtUtc is null
                    ? "Another work session on this ticket is still running. End it first."
                    : "This overlaps another work session on the same ticket.";
        }
        return null;
    }

    /// <summary>Total time of the finished sessions plus, for a running one, the time up to <paramref name="nowUtc"/>.</summary>
    public static TimeSpan Total(IEnumerable<Span> sessions, DateTime nowUtc) =>
        sessions.Aggregate(TimeSpan.Zero, (sum, s) =>
        {
            var end = s.EndedAtUtc ?? nowUtc;
            return end > s.StartedAtUtc ? sum + (end - s.StartedAtUtc) : sum;
        });
}
