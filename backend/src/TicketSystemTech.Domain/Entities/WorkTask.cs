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

    /// <summary>The stretches of work, started with the timer button or entered by hand. They drive the Gantt timeline blocks.</summary>
    public ICollection<WorkTaskSession> Sessions { get; set; } = new List<WorkTaskSession>();

    public ICollection<WorkTaskAssignmentLog> AssignmentHistory { get; set; } = new List<WorkTaskAssignmentLog>();

    /// <summary>Concurrency token: two people handing the same task over (or correcting its time) at once are told about the
    /// conflict (409) instead of the later save silently erasing the earlier one.</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
