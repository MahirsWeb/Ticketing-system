using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

/// <summary>Tracks which inbox messages a branch has flagged as ticket candidates, and whether converted. Shared by everyone in the branch.</summary>
public class EmailTicketMark : BaseEntity
{
    public Guid DepartmentId { get; set; }

    /// <summary>Staff member who last changed the mark, kept for audit purposes only.</summary>
    public Guid MarkedByUserId { get; set; }

    public string MessageId { get; set; } = string.Empty;
    public bool IsMarked { get; set; }
    public Guid? ConvertedTicketId { get; set; }
}
