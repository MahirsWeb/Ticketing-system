using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>A staff member assigned to work on a ticket. A ticket may have more than one — e.g. when two different people each resolved a different part of the request.</summary>
public class TicketAssignee : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid UserId { get; set; }

    public DateTime AssignedAtUtc { get; set; }

    /// <summary>
    /// True for the one person the ticket is "assigned to"; everybody else on the list is help. A ticket has at most one (a unique
    /// index says so) and none when nobody is on it. This list is the only record of who is on a ticket: there used to be a second
    /// column on the ticket itself, and the two could say different things.
    /// </summary>
    public bool IsPrimary { get; set; }
}
