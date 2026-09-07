using TicketSystemTech.Domain.Common;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Domain.Entities;

/// <summary>
/// A lightweight, informal work item colleagues hand to each other alongside ticket work
/// (e.g. "check the Bingo contract data"). Distinct from Ticket: no client, no SLA — just
/// title/description/assignment/time, tracked so it shows up on the Gantt timeline next to tickets.
/// </summary>
public class WorkTask : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Branch the task belongs to — inherited from whoever created it. Keeps tasks isolated per branch like tickets.</summary>
    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid CreatedByUserId { get; set; }

    /// <summary>Who most recently (re)assigned this task — the creator on first assignment, or whoever last handed it off.</summary>
    public Guid AssignedByUserId { get; set; }
    public Guid AssignedToUserId { get; set; }

    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Pending;

    /// <summary>Set manually or via the start/stop timer button. Drives the Gantt timeline block.</summary>
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }

    public ICollection<WorkTaskAssignmentLog> AssignmentHistory { get; set; } = new List<WorkTaskAssignmentLog>();
}
