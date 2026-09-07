using TicketSystemTech.Domain.Common;

namespace TicketSystemTech.Domain.Entities;

public class TicketAttachment : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>Set when the attachment belongs to a specific Response/InternalNote message rather than the original ticket.</summary>
    public Guid? MessageId { get; set; }
    public TicketMessage? Message { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
}
