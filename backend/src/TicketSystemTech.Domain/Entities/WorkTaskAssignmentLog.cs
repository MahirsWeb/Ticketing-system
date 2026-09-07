using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>Audit trail entry recorded every time a WorkTask is created or handed off to someone else — who assigned it to whom, and when.</summary>
public class WorkTaskAssignmentLog : BaseEntity
{
    public Guid WorkTaskId { get; set; }
    public WorkTask? WorkTask { get; set; }

    public Guid AssignedByUserId { get; set; }
    public Guid AssignedToUserId { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}
