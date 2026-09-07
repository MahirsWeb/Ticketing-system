using TicketSystemTech.Domain.Common;
using TicketSystemTech.Domain.Enums;

namespace TicketSystemTech.Domain.Entities;

/// <summary>A single chat entry on a ticket — either a client-visible Response or a staff-only InternalNote.</summary>
public class TicketMessage : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid AuthorId { get; set; }
    public MessageType Type { get; set; }
    public string BodyHtml { get; set; } = string.Empty;

    public ICollection<TicketAttachment> Attachments { get; set; } = new List<TicketAttachment>();
}
