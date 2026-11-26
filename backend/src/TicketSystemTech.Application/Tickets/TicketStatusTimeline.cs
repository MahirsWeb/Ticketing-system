using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Application.Tickets;

/// <summary>A row of the audit trail of a ticket, as much of it as the timeline needs.</summary>
public sealed record TrailEvent(DateTime AtUtc, string Action, string? Details);

/// <summary>A stretch of time a ticket was in one status. <see cref="Status"/> null: it cannot be told (the trail does not say). <see cref="ToUtc"/> null: it still is.</summary>
public sealed record StatusSpan(TicketStatus? Status, DateTime FromUtc, DateTime? ToUtc);

/// <summary>
/// How long a ticket was in each status, put together from its audit trail (<c>TicketOpened</c>, <c>TicketStatusChanged</c>, <c>TicketClosed</c>,
/// <c>TicketReopened</c>) and from the columns of the ticket for what the trail lacks. A stretch is only given a status when the trail says
/// it: where the trail is missing, contradicts itself or ends in another status than the ticket has now, the stretch is "unknown", never a guess.
/// </summary>
public sealed class StatusTimeline
{
    public StatusTimeline(IReadOnlyList<StatusSpan> spans) => Spans = spans;

    public IReadOnlyList<StatusSpan> Spans { get; }

    /// <summary>Time in each status up to <paramref name="asOfUtc"/> (the stretch that still goes on counts until then). A status the ticket was never known to be in is absent.</summary>
    public IReadOnlyDictionary<TicketStatus, TimeSpan> Totals(DateTime asOfUtc)
    {
        var totals = new Dictionary<TicketStatus, TimeSpan>();
        foreach (var span in Spans)
        {
            if (span.Status is not { } status) continue;
            totals[status] = totals.GetValueOrDefault(status) + Length(span, asOfUtc);
        }
        return totals;
    }

    /// <summary>The time that cannot be put in any status.</summary>
    public TimeSpan Unknown(DateTime asOfUtc) => Spans.Where(s => s.Status is null).Aggregate(TimeSpan.Zero, (sum, s) => sum + Length(s, asOfUtc));

    private static TimeSpan Length(StatusSpan span, DateTime asOfUtc)
    {
        var end = span.ToUtc ?? asOfUtc;
        return end > span.FromUtc ? end - span.FromUtc : TimeSpan.Zero;
    }
}

public static class TicketStatusTimelineBuilder
{
    /// <summary>The same row of the trail and the column of the ticket are one fact when they are this close.</summary>
    private static readonly TimeSpan SameMoment = TimeSpan.FromSeconds(5);

    private sealed record Transition(DateTime At, TicketStatus? From, TicketStatus? To);

    public static StatusTimeline Build(
        DateTime createdAtUtc, DateTime? openedAtUtc, DateTime? closedAtUtc, TicketStatus currentStatus, IEnumerable<TrailEvent> trail)
    {
        var rows = trail.ToList();
        var transitions = new List<Transition>();

        foreach (var row in rows)
        {
            switch (row.Action)
            {
                case "TicketOpened": transitions.Add(new Transition(row.AtUtc, TicketStatus.New, TicketStatus.Open)); break;
                case "TicketClosed": transitions.Add(new Transition(row.AtUtc, null, TicketStatus.Closed)); break;
                case "TicketReopened": transitions.Add(new Transition(row.AtUtc, TicketStatus.Closed, TicketStatus.Open)); break;
                case "TicketStatusChanged":
                    var change = TicketAuditDetails.StatusChange(row.Details);
                    // A row whose words are not understood still means "it changed": from then on the status is not known.
                    transitions.Add(change is { } c ? new Transition(row.AtUtc, c.From, c.To) : new Transition(row.AtUtc, null, null));
                    break;
            }
        }

        // What the trail lacks: a ticket made already open (on behalf of a client) has no TicketOpened row, and older tickets may lack others.
        if (openedAtUtc is { } opened && !rows.Any(r => r.Action == "TicketOpened"))
            transitions.Add(new Transition(opened, TicketStatus.New, TicketStatus.Open));
        if (currentStatus == TicketStatus.Closed && closedAtUtc is { } closed
            && !rows.Any(r => r.Action == "TicketClosed" && (r.AtUtc - closed).Duration() <= SameMoment))
            transitions.Add(new Transition(closed, null, TicketStatus.Closed));

        // Ordered by time; rows at the same instant keep the order they came in (OrderBy is stable). A row from before the ticket existed is clock skew.
        var ordered = transitions.Select(t => t with { At = t.At < createdAtUtc ? createdAtUtc : t.At }).OrderBy(t => t.At).ToList();

        var spans = new List<StatusSpan>();
        // The first stretch is "New" only when the trail itself says the ticket was New then; a ticket that nothing ever happened to is as it is now.
        TicketStatus? state = ordered.Count == 0 ? (currentStatus == TicketStatus.New ? TicketStatus.New : null)
            : ordered[0].From == TicketStatus.New ? TicketStatus.New : null;
        var since = createdAtUtc;

        foreach (var t in ordered)
        {
            // A row that says the ticket was in a status other than the one the rows before it led to: that stretch cannot be told.
            var stretch = t.From is not null && state is not null && state != t.From ? null : state;
            if (t.At > since) spans.Add(new StatusSpan(stretch, since, t.At));
            state = t.To;
            since = t.At;
        }

        // The trail ends in a status the ticket does not have: the end of it is missing.
        if (state is not null && state != currentStatus) state = null;
        spans.Add(new StatusSpan(state, since, null));

        return new StatusTimeline(spans);
    }
}
