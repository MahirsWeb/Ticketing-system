namespace TicketSystemTech.Application.Common.Interfaces;

/// <summary>Keeps ticket assignments consistent with who can actually work on them.</summary>
public interface ITicketAssignmentService
{
    /// <summary>
    /// Takes a staff member off every ticket that is still being worked on (Open, InProgress, Resolved) — used when
    /// their account is deactivated or they move to another branch/sub-branch/role. Before this, such a person stayed
    /// the owner of those tickets: nobody was reminded, overdue alerts went to a mailbox no one reads, and the tickets
    /// showed up under a name that could no longer act on them.
    ///
    /// The ticket gets a note in its activity feed, the next remaining assignee (if any) becomes the primary one, and
    /// a ticket left with nobody is reported to the active staff of its branch so it can be picked up.
    /// </summary>
    /// <param name="reason">Plain text, e.g. "account deactivated"; it is HTML-encoded before it reaches the feed.</param>
    /// <returns>How many tickets were released.</returns>
    Task<int> ReleaseLiveTicketsAsync(Guid userId, string reason, Guid? actorUserId, CancellationToken ct = default);
}
