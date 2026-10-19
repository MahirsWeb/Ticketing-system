using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Domain.Workflow;

/// <summary>
/// The one place that says which status changes are legal. Each endpoint used to carry its own ad-hoc checks, and
/// the InProgress and Resolved statuses could never actually be reached, although reports and the list counters
/// already grouped by them.
///
///   New ──open──▶ Open ◀──▶ InProgress ──▶ Resolved ──▶ Closed
///                   │            │             │           ▲
///                   └────────────┴─────────────┴───close────┘      Closed ──reopen──▶ Open
///
/// Closing needs a resolution summary and is its own endpoint; "reopen" is the only way out of Closed.
/// </summary>
public static class TicketWorkflow
{
    private static readonly IReadOnlyDictionary<TicketStatus, TicketStatus[]> Allowed =
        new Dictionary<TicketStatus, TicketStatus[]>
        {
            [TicketStatus.New] = new[] { TicketStatus.Open },
            [TicketStatus.Open] = new[] { TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed },
            [TicketStatus.InProgress] = new[] { TicketStatus.Open, TicketStatus.Resolved, TicketStatus.Closed },
            [TicketStatus.Resolved] = new[] { TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Closed },
            [TicketStatus.Closed] = new[] { TicketStatus.Open }
        };

    public static bool CanTransition(TicketStatus from, TicketStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Statuses staff can move a ticket to with the plain status endpoint. New is never a target, and
    /// Closed/reopen have their own endpoints (a summary is required to close).</summary>
    public static bool IsDirectlyAssignable(TicketStatus status) =>
        status is TicketStatus.Open or TicketStatus.InProgress or TicketStatus.Resolved;

    /// <summary>A ticket still being worked on: the SLA clock is running and it can be transferred or reassigned.</summary>
    public static bool IsActive(TicketStatus status) =>
        status is TicketStatus.Open or TicketStatus.InProgress or TicketStatus.Resolved;
}
