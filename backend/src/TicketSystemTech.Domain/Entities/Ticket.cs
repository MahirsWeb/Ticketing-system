using TicketSystemTech.Domain.Common;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Domain.Entities;

public class Ticket : BaseEntity
{
    /// <summary>Public-facing ticket number, e.g. "100042". Assigned on creation.</summary>
    public string TicketNumber { get; set; } = string.Empty;

    /// <summary>Original title from the client. Immutable once set.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Original HTML description from the client. Immutable once set.</summary>
    public string Description { get; set; } = string.Empty;

    public Guid ClientId { get; set; }
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }

    public TicketStatus Status { get; set; } = TicketStatus.New;

    // Filled in when a consultant opens the ticket.
    public TicketSource? Source { get; set; }
    public Guid? HelpTopicId { get; set; }
    public HelpTopic? HelpTopic { get; set; }
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>Set when the ticket's branch has sub-branches defined — a finer-grained routing/stats/assignment unit within the branch.</summary>
    public Guid? SubBranchId { get; set; }
    public SubBranch? SubBranch { get; set; }

    public Guid? SlaPlanId { get; set; }
    public SlaPlan? SlaPlan { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public Guid? AssignedToUserId { get; set; }

    /// <summary>Internal categorization set by staff when opening the ticket, picked from an Admin-managed list. Staff/Admin only — never shown to the client.</summary>
    public Guid? CategoryId { get; set; }
    public TicketCategory? Category { get; set; }

    /// <summary>Urgency chosen by whoever opens the ticket. Drives the default list sort order.</summary>
    public TicketPriority? Priority { get; set; }

    public Guid? OpenedByUserId { get; set; }
    public DateTime? OpenedAtUtc { get; set; }

    public Guid? ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>Mandatory detailed resolution description, required to close the ticket.</summary>
    public string? ResolutionSummary { get; set; }


    /// <summary>Set once an overdue notification has been sent, so the background job doesn't re-notify every run.</summary>
    public DateTime? OverdueNotifiedAtUtc { get; set; }

    /// <summary>SQL Server rowversion. A save that touches a ticket someone else changed in the meantime fails (and the
    /// API answers 409) instead of silently overwriting their change: two staff opening or closing the same ticket at
    /// once used to both "win", with the later write erasing the earlier one.</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<TicketMessage> Messages { get; set; } = new List<TicketMessage>();
    public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();
    public ICollection<TicketAssignee> Assignees { get; set; } = new List<TicketAssignee>();

    /// <summary>The stretches of work done on the ticket (see <see cref="TicketWorkSession"/>).</summary>
    public ICollection<TicketWorkSession> WorkSessions { get; set; } = new List<TicketWorkSession>();
}
