using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>
/// One stretch of work on a ticket. A ticket has as many as it needs (the morning and the afternoon are two sessions),
/// and each belongs to the person who did the work: the ticket's main assignee at the time it was logged.
/// People added to a ticket as extra help have no sessions of their own: their involvement is the assignee list, not time.
/// </summary>
public class TicketWorkSession : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>Who did the work. Stays with the session if the ticket is later given to somebody else.</summary>
    public Guid UserId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    /// <summary>Null while the session is still running; it is closed when the ticket is closed, handed over or released.</summary>
    public DateTime? EndedAtUtc { get; set; }
}
