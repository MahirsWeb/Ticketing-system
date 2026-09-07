using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>A staff member assigned to work on a ticket. A ticket may have more than one — e.g. when two different people each resolved a different part of the request.</summary>
public class TicketAssignee : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid UserId { get; set; }

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}
